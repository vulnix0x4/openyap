#pragma once
#include <CoreAudio/CoreAudio.h>
typedef struct Router Router;
Router *router_create(void);
void router_destroy(Router *r);
// Sources: music=0, mic=1, game=2. Sinks: headphones=0, BlackHole=1.
OSStatus router_input(Router *r, int source, AudioDeviceID device);
OSStatus router_output(Router *r, int sink, AudioDeviceID device);
OSStatus router_verify(Router *r, AudioDeviceID blackhole);
void router_stop(Router *r);
void router_music_boost(Router *r, int enabled); // optional +6 dB shared music
void router_gain(Router *r, int control,
                 float gain); // music hear, music send, mic, game, master
void router_pause(Router *r, int paused);
int router_is_paused(Router *r);
float router_meter(
    Router *r,
    int meter); // music, mic, game, listening, send, BlackHole return
unsigned long router_dropouts(Router *r);
int router_selftest(void);

// Immutable stereo 48 kHz clips, at most 15 seconds each. -1 stops playback.
// Loading/triggering use the same control thread; clips live until router destruction.
int router_sound_load(Router *r, const float *samples, unsigned frames);
void router_sound_play(Router *r, int slot);
void router_sound_gain(Router *r, int sink, float gain);
int router_sound_playing(Router *r);
float router_sound_meter(Router *r, int sink);
