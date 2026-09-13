// Immutable clip bank. One UI-thread producer publishes clips; each hardware
// output owns its playback cursor. Clips are freed only after IOProcs stop.
#define SB_SLOTS 64
#define SB_MAX_FRAMES (48000 * 15)
#define SB_TOTAL_FRAMES (48000 * 180)
typedef struct { unsigned frames; float data[]; } SBClip;
typedef struct {
  uint64_t command;
  double position;
  float gain;
  float filter[256][32];
} SBPlayer;
typedef struct {
  _Atomic(SBClip *) clips[SB_SLOTS];
  _Atomic uint64_t command, playing[2];
  _Atomic float gain[2], meter[2];
  SBPlayer player[2];
  unsigned totalFrames;
} Soundboard;
static void sb_prepare(Soundboard *b, int sink, double rate) {
  double cutoff = .94 / fmax(1, 48000 / rate);
  for (int p = 0; p < 256; p++) {
    double sum = 0;
    for (int t = 0; t < 32; t++) {
      double x = t - 15 - (double)p / 256;
      double sinc = fabs(x) < 1e-9 ? cutoff : sin(M_PI * cutoff * x) / (M_PI * x);
      double window = .42 - .5 * cos(2 * M_PI * t / 31) + .08 * cos(4 * M_PI * t / 31);
      b->player[sink].filter[p][t] = sinc * window;
      sum += sinc * window;
    }
    for (int t = 0; t < 32; t++) b->player[sink].filter[p][t] /= sum;
  }
}
static int sb_load(Soundboard *b, const float *data, unsigned frames) {
  if (!data || !frames || frames > SB_MAX_FRAMES || frames > SB_TOTAL_FRAMES - b->totalFrames) return -1;
  int slot = 0;
  while (slot < SB_SLOTS && atomic_load(&b->clips[slot])) slot++;
  if (slot == SB_SLOTS) return -1;
  SBClip *clip = malloc(sizeof(SBClip) + frames * 2 * sizeof(float));
  if (!clip) return -1;
  clip->frames = frames;
  for (unsigned i = 0; i < frames * 2; i++) clip->data[i] = isfinite(data[i]) ? fmaxf(-1, fminf(1, data[i])) : 0;
  b->totalFrames += frames;
  atomic_store_explicit(&b->clips[slot], clip, memory_order_release);
  return slot;
}
static void sb_trigger(Soundboard *b, int slot) {
  if (slot < -1 || slot >= SB_SLOTS || (slot >= 0 && !atomic_load(&b->clips[slot]))) return;
  uint64_t old = atomic_load(&b->command);
  atomic_store(&b->command, (((old >> 8) + 1) << 8) | (uint64_t)(slot + 1));
}
static void sb_sample(Soundboard *b, int sink, double rate, int paused, float *out) {
  out[0] = out[1] = 0;
  SBPlayer *p = &b->player[sink];
  uint64_t command = atomic_load(&b->command);
  if (p->command != command) { p->command = command; p->position = 0; }
  int slot = (int)(command & 255) - 1;
  if (paused) { p->position = SB_MAX_FRAMES; atomic_store(&b->playing[sink], 0); return; }
  SBClip *clip = slot >= 0 && slot < SB_SLOTS ? atomic_load_explicit(&b->clips[slot], memory_order_acquire) : NULL;
  if (!clip || p->position >= clip->frames) { atomic_store(&b->playing[sink], 0); return; }
  atomic_store(&b->playing[sink], command);
  float target = atomic_load(&b->gain[sink]);
  p->gain = target == 0 ? 0 : p->gain + (target - p->gain) * .002f;
  int frame = (int)p->position, phase = (int)((p->position - frame) * 256);
  for (int t = 0; t < 32; t++) {
    int index = frame + t - 15;
    if (index < 0 || index >= (int)clip->frames) continue;
    for (int c = 0; c < 2; c++) out[c] += clip->data[index * 2 + c] * p->filter[phase][t] * p->gain;
  }
  p->position += 48000 / rate;
}
static void sb_free(Soundboard *b) {
  for (int i = 0; i < SB_SLOTS; i++) free(atomic_load(&b->clips[i]));
}
