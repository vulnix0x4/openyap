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
