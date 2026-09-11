#include "../Sources/AudioCore/AudioCore.c"
static int failures = 0;
#define CHECK(x, msg)                                                          \
  do {                                                                         \
    if (!(x)) {                                                                \
      fprintf(stderr, "FAIL: %s\n", msg);                                      \
      failures++;                                                              \
    } else                                                                     \
      printf("PASS: %s\n", msg);                                               \
  } while (0)
static Router *fixture(void) {
  Router *r = router_create();
  for (int s = 0; s < 3; s++) {
    r->input[s].rate = 48000;
    for (int k = 0; k < 2; k++)
      prepare(&r->ring[k][s], 1);
  }
  return r;
}
static void gains(Router *r, float a, float b, float c, float d, float e) {
  float v[] = {a, b, c, d, e};
  for (int i = 0; i < 5; i++) {
    router_gain(r, i, v[i]);
    for (int k = 0; k < 2; k++)
      r->smooth[k][i] = v[i];
  }
}
static void feed(Router *r, int src, float value) {
  float v[512];
  for (int i = 0; i < 512; i++)
    v[i] = value;
  AudioBufferList b = {1, {{2, sizeof(v), v}}};
  Endpoint e = {.r = r, .index = src};
  for (int i = 0; i < 14; i++)
    callback(0, 0, &b, 0, 0, 0, &e);
}
static float render(Router *r, int sink) {
  float v[512];
  AudioBufferList b = {1, {{2, sizeof(v), v}}};
  Endpoint e = {.r = r, .index = sink, .output = 1, .rate = 48000};
  callback(0, 0, 0, 0, &b, 0, &e);
  return v[100];
}
static int closeTo(float a, float b) { return fabsf(a - b) < 0.0002; }
int main(void) {
  CHECK(router_selftest() == 0, "basic core self-test");
  Router *r = fixture();
  feed(r, 0, .2);
  feed(r, 1, .3);
  feed(r, 2, .4);
  gains(r, .1, .6, .5, .7, .8);
  CHECK(closeTo(render(r, 0), .24), "music + game listening equation");
  CHECK(closeTo(render(r, 1), .27), "music + voice sending equation");
  gains(r, 0, .6, .5, .7, .8);
  CHECK(closeTo(render(r, 0), .224), "music listening mute leaves game");
  CHECK(closeTo(render(r, 1), .27),
        "music listening mute leaves sending untouched");
  gains(r, .1, 0, .5, .7, .8);
  CHECK(closeTo(render(r, 1), .15), "music send mute leaves mic");
  CHECK(closeTo(render(r, 0), .24),
        "music send mute leaves listening untouched");
  gains(r, .1, .6, 0, .7, .8);
  CHECK(closeTo(render(r, 1), .12), "voice mute/exclusion leaves music");
  gains(r, .1, .6, .5, 0, .8);
  CHECK(closeTo(render(r, 0), .016), "game mute leaves local music");
  CHECK(closeTo(render(r, 1), .27), "game mute cannot affect sending");
  gains(r, .1, .6, .5, .7, 0);
  CHECK(render(r, 0) == 0, "master mute exact silence");
  CHECK(closeTo(render(r, 1), .27), "master mute cannot affect sending");
  router_pause(r, 1);
  CHECK(render(r, 0) == 0 && render(r, 1) == 0,
        "device interruption silences both buses");
  router_destroy(r);
  r = fixture();
  feed(r, 2, 1);
  gains(r, 1, 1, 1, 1, 1);
  CHECK(render(r, 1) == 0, "game-only input has no path to virtual microphone");
  router_destroy(r);
  r = fixture();
  feed(r, 1, 1);
  gains(r, 1, 1, 1, 1, 1);
  CHECK(render(r, 0) == 0, "microphone has no monitoring feedback path");
  router_destroy(r);
  r = fixture();
  feed(r, 0, 1);
  feed(r, 1, 1);
  gains(r, 1, 1, 1, 1, 1);
  CHECK(render(r, 1) <= .981, "two full-scale sources cannot clip output");
  router_gain(r, 0, NAN);
  CHECK(atomic_load(&r->gain[0]) == 0, "nonfinite gain rejected");
  router_gain(r, 0, 40);
  CHECK(atomic_load(&r->gain[0]) == 1, "gain capped at unity");
  router_destroy(r);
  // Long asynchronous producer/consumer simulation with 44.1 -> 48k conversion,
  // 120ppm independent clock drift, and wraparound. Real resampler/ring code.
  r = fixture();
  Ring *q = &r->ring[0][0];
  prepare(q, 44100.0 / 48000);
  double source = 0, peak = 0;
  int nonzero = 0;
  for (int i = 0; i < 2200; i++)
    push(q, .2, .2);
  for (int block = 0; block < 20000; block++) {
    source += 256 * 44100.0 / 48000 * 1.00012;
    int n = (int)source;
    source -= n;
    for (int i = 0; i < n; i++)
      push(q, .2, .2);
    for (int i = 0; i < 256; i++) {
      float v[2];
      pull(r, q, 44100.0 / 48000, 44100, v);
      peak = fmax(peak, fabs(v[0]));
      nonzero += fabs(v[0]) > .1;
    }
  }
  CHECK(peak < .201 && nonzero > 5100000,
        "44.1/48 kHz conversion, clock drift and ring wrap (106 seconds)");
  CHECK(router_dropouts(r) == 0,
        "drift compensation stays buffered without recovery");
  router_destroy(r);
  r = fixture();
  float v[2];
  pull(r, &r->ring[0][0], 1, 48000, v);
  CHECK(v[0] == 0 && v[1] == 0, "empty ring yields silence");
  for (int i = 0; i < CAP * 2; i++)
    push(&r->ring[0][0], .2, .2);
  pull(r, &r->ring[0][0], 1, 48000, v);
  CHECK(router_dropouts(r) == 1 && isfinite(v[0]),
        "overrun bounds memory and recovers recent audio");
  router_destroy(r);
  float left[4] = {.1, .2, .3, .4}, right[4] = {.4, .3, .2, .1};
  struct {
    UInt32 n;
    AudioBuffer b[2];
  } planar = {2, {{1, sizeof(left), left}, {1, sizeof(right), right}}};
  CHECK(closeTo(sample((AudioBufferList *)&planar, 2, 0), .3) &&
            closeTo(sample((AudioBufferList *)&planar, 2, 1), .2),
        "noninterleaved channel layout");
  r = fixture();
  q = &r->ring[0][0];
  prepare(q, 48000.0 / 44100);
  for (int i = 0; i < 8000; i++) {
    float x = .2 * sin(2 * M_PI * 1000 * i / 48000);
    push(q, x, x);
  }
  double energy = 0;
  for (int i = 0; i < 2000; i++) {
    float x[2];
    pull(r, q, 48000.0 / 44100, 48000, x);
    energy += x[0] * x[0];
  }
  CHECK(fabs(sqrt(energy / 2000) - .2 / sqrt(2)) < .002,
        "1 kHz sine amplitude preserved by 48 -> 44.1 kHz conversion");
  router_destroy(r);
  r = fixture();
  q = &r->ring[0][0];
  prepare(q, 3);
  for (int i = 0; i < 8000; i++) {
    float x = .2 * sin(2 * M_PI * 12000 * i / 48000);
    push(q, x, x);
  }
  energy = 0;
  for (int i = 0; i < 1200; i++) {
    float x[2];
    pull(r, q, 3, 48000, x);
    energy += x[0] * x[0];
  }
  CHECK(sqrt(energy / 1200) < .001,
        "48 -> 16 kHz conversion rejects above-Nyquist content");
  router_destroy(r);
  r = fixture();
  r->input[2].rate = 0; // No call tap in direct-playback mode.
  feed(r, 0, .2); feed(r, 1, .3); gains(r, .1, .6, .5, 1, .8);
  CHECK(closeTo(render(r, 0), .016), "direct-playback mode monitors only music without a call source");
  CHECK(closeTo(render(r, 1), .27), "direct-playback mode still sends the full music/voice mix");
  router_destroy(r);
  printf("\n%d failures\n", failures);
  return failures ? 1 : 0;
}
