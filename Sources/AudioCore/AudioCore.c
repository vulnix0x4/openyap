#include "AudioCore.h"
#include <math.h>
#include <stdatomic.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#define CAP 32768
#define TAPS 32
#define PHASES 256
// Each ring has exactly one capture producer and one output consumer.
typedef struct {
  float data[CAP][2];
  _Atomic uint64_t write, read;
  double phase;
  int primed;
  float filter[PHASES][TAPS];
} Ring;
typedef struct {
  struct Router *r;
  AudioDeviceID device;
  AudioDeviceIOProcID proc;
  double rate;
  int index, output;
  int listening;
} Endpoint;
struct Router {
  Ring ring[2][3];
  Endpoint input[4], output[2];
  _Atomic float gain[5], meter[6];
  _Atomic int paused;
  _Atomic unsigned long drops;
  float smooth[2][5];
};
static float clampf(float x, float a, float b) { return fmaxf(a, fminf(b, x)); }
static float protect(float x) {
  if (!isfinite(x))
    return 0;
  float a = fabsf(x);
  return a <= 0.8f ? x : copysignf(0.8f + 0.18f * tanhf((a - 0.8f) / 0.18f), x);
}
static unsigned frames(const AudioBufferList *b) {
  if (!b || !b->mNumberBuffers || !b->mBuffers[0].mNumberChannels)
    return 0;
  return b->mBuffers[0].mDataByteSize / (4 * b->mBuffers[0].mNumberChannels);
}
static float sample(const AudioBufferList *b, unsigned f, unsigned c) {
  unsigned channel = c;
  for (unsigned i = 0; i < b->mNumberBuffers; i++) {
    const AudioBuffer *v = &b->mBuffers[i];
    if (channel < v->mNumberChannels) {
      if (!v->mData ||
          (f * v->mNumberChannels + channel + 1) * 4 > v->mDataByteSize)
        return 0;
      float x = ((float *)v->mData)[f * v->mNumberChannels + channel];
      return isfinite(x) ? x : 0;
    }
    channel -= v->mNumberChannels;
  }
  return c ? sample(b, f, 0) : 0;
}
static void put(AudioBufferList *b, unsigned f, unsigned c, float x) {
  unsigned channel = c;
  for (unsigned i = 0; i < b->mNumberBuffers; i++) {
    AudioBuffer *v = &b->mBuffers[i];
    if (channel < v->mNumberChannels) {
      if (v->mData &&
          (f * v->mNumberChannels + channel + 1) * 4 <= v->mDataByteSize)
        ((float *)v->mData)[f * v->mNumberChannels + channel] = x;
      return;
    }
    channel -= v->mNumberChannels;
  }
}
static void push(Ring *q, float l, float rr) {
  uint64_t w = atomic_load_explicit(&q->write, memory_order_relaxed),
           r = atomic_load_explicit(&q->read, memory_order_acquire);
  if (w - r >= CAP - 1)
    return;
  q->data[w % CAP][0] = l;
  q->data[w % CAP][1] = rr;
  atomic_store_explicit(&q->write, w + 1, memory_order_release);
}
static void prepare(Ring *q, double ratio) {
  double cutoff = 0.94 / fmax(1, ratio);
  for (int p = 0; p < PHASES; p++) {
    double sum = 0;
    for (int t = 0; t < TAPS; t++) {
      double x = t - 15 - (double)p / PHASES;
      double sinc =
          fabs(x) < 1e-9 ? cutoff : sin(M_PI * cutoff * x) / (M_PI * x);
      double window = 0.42 - 0.5 * cos(2 * M_PI * t / (TAPS - 1)) +
                      0.08 * cos(4 * M_PI * t / (TAPS - 1));
      q->filter[p][t] = sinc * window;
      sum += q->filter[p][t];
    }
    for (int t = 0; t < TAPS; t++)
      q->filter[p][t] /= sum;
  }
}
static void pull(Router *r, Ring *q, double ratio, double rate, float *out) {
  out[0] = out[1] = 0;
  uint64_t rd = atomic_load_explicit(&q->read, memory_order_relaxed),
           w = atomic_load_explicit(&q->write, memory_order_acquire);
  uint64_t n = w - rd;
  double target = rate * 0.045;
  if (!q->primed) {
    if (n < target)
      return;
    q->primed = 1;
    q->phase = 0;
  }
  if (n > rate * 0.18) {
    rd = w - (uint64_t)target;
    q->phase = 0;
    n = w - rd;
    atomic_store(&q->read, rd);
    atomic_fetch_add(&r->drops, 1);
  }
  if (n < TAPS + 2) {
    q->primed = 0;
    atomic_fetch_add(&r->drops, 1);
    return;
  }
  // Slowly adapt each consumer to its own hardware clock. No shared output
  // clock.
  double correction =
      clampf((float)((n - target) / target * 0.002), -0.005, 0.005);
  int phase = (int)(q->phase * PHASES);
  for (int c = 0; c < 2; c++)
    for (int t = 0; t < TAPS; t++)
      out[c] += q->data[(rd + t) % CAP][c] * q->filter[phase][t];
  q->phase += ratio * (1 + correction);
  uint64_t step = (uint64_t)q->phase;
  q->phase -= step;
  atomic_store_explicit(&q->read, rd + (step < n ? step : n),
                        memory_order_release);
}
static OSStatus callback(AudioDeviceID d, const AudioTimeStamp *t,
                         const AudioBufferList *in, const AudioTimeStamp *it,
                         AudioBufferList *out, const AudioTimeStamp *ot,
                         void *ctx) {
  Endpoint *e = ctx;
  Router *r = e->r;
  (void)d;
  (void)t;
  (void)it;
  (void)ot;
  if (!e->output && e->index == 3) {
    float peak = 0;
    for (unsigned f = 0; f < frames(in); f++)
      peak =
          fmaxf(peak, fmaxf(fabsf(sample(in, f, 0)), fabsf(sample(in, f, 1))));
    atomic_store(&r->meter[5], peak);
    return noErr;
  }
  if (!e->output) {
    float peak = 0;
    for (unsigned f = 0; f < frames(in); f++) {
      float l = sample(in, f, 0), rr = sample(in, f, 1);
      if (e->index == 1)
        l = rr = (l + rr) * 0.5f;
      peak = fmaxf(peak, fmaxf(fabsf(l), fabsf(rr)));
      if (e->index != 2)
        push(&r->ring[1][e->index], l, rr);
      if (e->index != 1)
        push(&r->ring[0][e->index], l, rr);
    }
    atomic_store(&r->meter[e->index], peak);
    return noErr;
  }
  if (out)
    for (unsigned b = 0; b < out->mNumberBuffers; b++)
      if (out->mBuffers[b].mData)
        memset(out->mBuffers[b].mData, 0, out->mBuffers[b].mDataByteSize);

  float peak = 0;
  int sink = e->index;
  for (unsigned f = 0; f < frames(out); f++) {
    float s[3][2] = {{0}};
    for (int src = 0; src < 3; src++) {
      if ((sink == 0 && src == 1) || (sink == 1 && src == 2))
        continue;
      double rate = r->input[src].rate;
      if (rate > 0)
        pull(r, &r->ring[sink][src], rate / e->rate, rate, s[src]);
    }
    for (int g = 0; g < 5; g++) {
      float target =
          atomic_load(&r->gain[g]); // exact mute, ramp unmute/gain changes
      r->smooth[sink][g] =
          target == 0
              ? 0
              : r->smooth[sink][g] + (target - r->smooth[sink][g]) * 0.002f;
    }
    for (int c = 0; c < 2; c++) {
      float x =
          sink == 0
              ? (s[0][c] * r->smooth[sink][0] + s[2][c] * r->smooth[sink][3]) *
                    r->smooth[sink][4]
              : s[0][c] * r->smooth[sink][1] + s[1][c] * r->smooth[sink][2];
      x = atomic_load(&r->paused) ? 0 : protect(x);
      put(out, f, c, x);
      peak = fmaxf(peak, fabsf(x));
    }
  }
  atomic_store(&r->meter[3 + sink], peak);
  return noErr;
}
static OSStatus deviceChanged(AudioObjectID object, UInt32 count,
                              const AudioObjectPropertyAddress *addresses,
                              void *context) {
  Endpoint *e = context;
  (void)object;
  (void)count;
  (void)addresses;
  atomic_store(&e->r->paused, 1);
  return noErr;
}
static OSStatus start(Endpoint *e, Router *r, int index, int output,
                      AudioDeviceID device) {
  e->r = r;
  e->index = index;
  e->output = output;
  e->device = device;
  AudioObjectPropertyAddress a = {kAudioDevicePropertyStreamFormat,
                                  output ? kAudioObjectPropertyScopeOutput
                                         : kAudioObjectPropertyScopeInput,
                                  kAudioObjectPropertyElementMain};
  AudioStreamBasicDescription f = {0};
  UInt32 n = sizeof(f);
  OSStatus s = AudioObjectGetPropertyData(device, &a, 0, NULL, &n, &f);
  if (s)
    return s;
  if (f.mFormatID != kAudioFormatLinearPCM ||
      !(f.mFormatFlags & kAudioFormatFlagIsFloat) || f.mBitsPerChannel != 32 ||
      f.mSampleRate < 8000 || f.mSampleRate > 192000)
    return kAudioDeviceUnsupportedFormatError;
  e->rate = f.mSampleRate;
  if (output)
    for (int src = 0; src < 3; src++)
      prepare(&r->ring[index][src],
              r->input[src].rate > 0 ? r->input[src].rate / e->rate : 1);
  s = AudioDeviceCreateIOProcID(device, callback, e, &e->proc);
  if (s)
    return s; // Disable unused streams: opening an output must never activate a
              // Bluetooth mic.
  AudioObjectPropertyAddress sa = {kAudioDevicePropertyStreams,
                                   output ? kAudioObjectPropertyScopeInput
                                          : kAudioObjectPropertyScopeOutput,
                                   kAudioObjectPropertyElementMain};
  UInt32 bytes = 0;
  if (AudioObjectGetPropertyDataSize(device, &sa, 0, NULL, &bytes) == 0 &&
      bytes > 0) {
    UInt32 count = bytes / sizeof(AudioStreamID);
    UInt32 usageBytes = offsetof(AudioHardwareIOProcStreamUsage, mStreamIsOn) +
                        count * sizeof(UInt32);
    AudioHardwareIOProcStreamUsage *u = calloc(1, usageBytes);
    u->mIOProc = (void *)e->proc;
    u->mNumberStreams = count;
    sa.mSelector = kAudioDevicePropertyIOProcStreamUsage;
    OSStatus disabled =
        AudioObjectSetPropertyData(device, &sa, 0, NULL, usageBytes, u);
    free(u);
    if (disabled) {
      AudioDeviceDestroyIOProcID(device, e->proc);
      e->proc = NULL;
      return disabled;
    }
  }
  s = AudioDeviceStart(device, e->proc);
  if (s) {
    AudioDeviceDestroyIOProcID(device, e->proc);
    e->proc = NULL;
  } else {
    AudioObjectPropertyAddress change = {kAudioDevicePropertyDeviceIsAlive,
                                         kAudioObjectPropertyScopeGlobal,
                                         kAudioObjectPropertyElementMain};
    AudioObjectAddPropertyListener(device, &change, deviceChanged, e);
    change.mSelector = kAudioDevicePropertyNominalSampleRate;
    AudioObjectAddPropertyListener(device, &change, deviceChanged, e);
    e->listening = 1;
  }
  return s;
}
Router *router_create(void) {
  Router *r = calloc(1, sizeof(Router));
  if (r) {
    float g[] = {.25, .65, .7, .8, .65};
    for (int i = 0; i < 5; i++)
      atomic_store(&r->gain[i], g[i]);
  }
  return r;
}
OSStatus router_input(Router *r, int s, AudioDeviceID d) {
  return start(&r->input[s], r, s, 0, d);
}
OSStatus router_verify(Router *r, AudioDeviceID d) {
  if (r->input[3].proc)
    return noErr;
  return start(&r->input[3], r, 3, 0, d);
}
OSStatus router_output(Router *r, int s, AudioDeviceID d) {
  return start(&r->output[s], r, s, 1, d);
}
void router_gain(Router *r, int i, float g) {
  if (r && i >= 0 && i < 5)
    atomic_store(&r->gain[i], isfinite(g) ? clampf(g, 0, 1) : 0);
}
void router_pause(Router *r, int p) {
  if (r)
    atomic_store(&r->paused, p);
}
int router_is_paused(Router *r) { return r ? atomic_load(&r->paused) : 1; }
float router_meter(Router *r, int m) {
  return r ? atomic_load(&r->meter[m]) : 0;
}
unsigned long router_dropouts(Router *r) {
  return r ? atomic_load(&r->drops) : 0;
}
static void unlisten(Endpoint *e) {
  if (!e->listening)
    return;
  AudioObjectPropertyAddress a = {kAudioDevicePropertyDeviceIsAlive,
                                  kAudioObjectPropertyScopeGlobal,
                                  kAudioObjectPropertyElementMain};
  AudioObjectRemovePropertyListener(e->device, &a, deviceChanged, e);
  a.mSelector = kAudioDevicePropertyNominalSampleRate;
  AudioObjectRemovePropertyListener(e->device, &a, deviceChanged, e);
  e->listening = 0;
}
void router_stop(Router *r) {
  if (!r)
    return;
  router_pause(r, 1);
  for (int i = 0; i < 2; i++)
    if (r->output[i].proc) {
      unlisten(&r->output[i]);
      AudioDeviceStop(r->output[i].device, r->output[i].proc);
      AudioDeviceDestroyIOProcID(r->output[i].device, r->output[i].proc);
      r->output[i].proc = NULL;
    }
  for (int i = 0; i < 4; i++)
    if (r->input[i].proc) {
      unlisten(&r->input[i]);
      AudioDeviceStop(r->input[i].device, r->input[i].proc);
      AudioDeviceDestroyIOProcID(r->input[i].device, r->input[i].proc);
      r->input[i].proc = NULL;
    }
}
void router_destroy(Router *r) {
  if (r) {
    router_stop(r);
    free(r);
  }
}
int router_selftest(void) {
  int fail = 0;
  Router *r = router_create();
  float g[] = {.2, .7, .4, .8, .5};
  for (int i = 0; i < 5; i++) {
    router_gain(r, i, g[i]);
    for (int j = 0; j < 2; j++)
      r->smooth[j][i] = g[i];
  }
  for (int i = 0; i < 3; i++) {
    r->input[i].rate = 48000;
    for (int j = 0; j < 2; j++)
      prepare(&r->ring[j][i], 1);
  }
  float in[256 * 2], out[256 * 2];
  AudioBufferList ib = {1, {{2, sizeof(in), in}}},
                  ob = {1, {{2, sizeof(out), out}}};
  for (int src = 0; src < 3; src++) {
    for (int i = 0; i < 512; i++)
      in[i] = (src + 1) * .1f;
    Endpoint e = {.r = r, .index = src};
    for (int b = 0; b < 12; b++)
      callback(0, 0, &ib, 0, 0, 0, &e);
  }
  Endpoint a = {.r = r, .index = 0, .output = 1, .rate = 48000},
           b = {.r = r, .index = 1, .output = 1, .rate = 48000};
  callback(0, 0, 0, 0, &ob, 0, &a);
  if (fabsf(out[100] - .13f) > .001)
    fail++;
  callback(0, 0, 0, 0, &ob, 0, &b);
  if (fabsf(out[100] - .15f) > .001)
    fail++;
  router_gain(r, 4, 0);
  callback(0, 0, 0, 0, &ob, 0, &a);
  if (out[100] != 0)
    fail++;
  callback(0, 0, 0, 0, &ob, 0, &b);
  if (fabsf(out[100] - .15f) > .001)
    fail++;
  router_gain(r, 1, 0);
  router_gain(r, 2, 0);
  callback(0, 0, 0, 0, &ob, 0, &b);
  if (out[100] != 0)
    fail++;
  for (int i = -1000; i <= 1000; i++)
    if (fabsf(protect(i * .01f)) > .981f)
      fail++;
  if (protect(NAN) != 0)
    fail++;
  router_pause(r, 1);
  callback(0, 0, 0, 0, &ob, 0, &a);
  if (out[100] != 0)
    fail++;
  printf("DSP self-test: %s (%d failures)\n", fail ? "FAIL" : "PASS", fail);
  router_destroy(r);
  return fail;
}
