#include "../Sources/AudioCore/AudioCore.c"
#include <assert.h>
static float output(Router *r, int sink, double rate) {
  float samples[512];
  AudioBufferList b = {1, {{2, sizeof(samples), samples}}};
  Endpoint endpoint = {.r=r, .index=sink, .output=1, .rate=rate};
  callback(0,0,0,0,&b,0,&endpoint);
  return samples[256];
}
int main(void) {
  Router *r = router_create();
  float *samples = calloc(48000 * 2, sizeof(float));
  for (int i=0;i<48000*2;i++) samples[i]=.4f;
  assert(router_sound_load(r,NULL,20)==-1);
  assert(router_sound_load(r,samples,SB_MAX_FRAMES+1)==-1);
  int slot=router_sound_load(r,samples,48000); assert(slot==0);
  router_sound_gain(r,0,.25); router_sound_gain(r,1,.75);
  r->board.player[0].gain=.25; r->board.player[1].gain=.75;
  router_gain(r,4,.5);r->smooth[0][4]=.5;
  router_gain(r,1,1);r->smooth[1][1]=1;
  router_sound_play(r,slot);
  assert(fabs(output(r,0,48000)-.05)<.001);
  assert(fabs(output(r,1,48000)-.3)<.001);
  assert(router_sound_playing(r));
  puts("PASS: effects reach both outputs at independent levels, with listening master applied");
  router_gain(r,4,0);
  assert(output(r,0,48000)==0);
  assert(fabs(output(r,1,48000)-.3)<.001);
  router_gain(r,1,.5);r->smooth[1][1]=.5;
  assert(fabs(output(r,1,48000)-.15)<.001);
  router_music_boost(r,1);router_gain(r,1,1);
  float boosted=0;
  for(int i=0;i<12;i++) boosted=output(r,1,48000);
  assert(fabs(boosted-.6)<.003);
  router_gain(r,1,0);assert(output(r,1,48000)==0);
  router_gain(r,4,.5);r->smooth[0][4]=.5;
  assert(fabs(output(r,0,48000)-.05)<.001);
  puts("PASS: shared slider scales effects, Max boosts effects, shared mute is immediate, local effects stay independent");
  router_music_boost(r,0);router_gain(r,1,1);r->smooth[1][1]=1;
  router_sound_gain(r,1,0); assert(output(r,1,48000)==0);
  router_sound_gain(r,1,1);
  router_sound_play(r,-1);assert(!router_sound_playing(r));
  assert(output(r,1,48000)==0);
  puts("PASS: zero gain and Stop sounds silence effects immediately");
  router_sound_play(r,slot); output(r,1,48000);
  assert(r->board.player[1].position==256);
  router_sound_play(r,slot); output(r,1,48000);
  assert(r->board.player[1].position==256);
  router_pause(r,1);assert(output(r,1,48000)==0);
  router_pause(r,0);assert(output(r,1,48000)==0);
  puts("PASS: retrigger restarts, interruption stops without later replay");
  router_sound_play(r,slot); sb_prepare(&r->board,0,44100);
  float v[2];
  for(int i=0;i<44100;i++) sb_sample(&r->board,0,44100,0,v);
  assert(fabs(r->board.player[0].position-48000)<1.1);
  assert(r->board.player[1].position>=SB_MAX_FRAMES);
  puts("PASS: each output has an independent cursor and sample-rate conversion");
  // Test the soundboard's anti-alias filter at a 16 kHz output rate.
  for(int i=0;i<48000;i++) samples[i*2]=samples[i*2+1]=.4*sin(2*M_PI*12000*i/48000);
  int high=router_sound_load(r,samples,48000); assert(high==1);
  router_sound_play(r,high);sb_prepare(&r->board,1,16000);double energy=0;
  for(int i=0;i<2000;i++){sb_sample(&r->board,1,16000,0,v);if(i>100)energy+=v[0]*v[0];}
  assert(sqrt(energy/1899)<.001);
  puts("PASS: soundboard downsampling rejects above-Nyquist content");
  // Multiple loaded clips are immutable; nonfinite imported data is sanitized.
  samples[0]=NAN; samples[1]=INFINITY;
  int invalid=router_sound_load(r,samples,100);assert(invalid==2);
  assert(atomic_load(&r->board.clips[invalid])->data[0]==0);
  assert(atomic_load(&r->board.clips[invalid])->data[1]==0);
  assert(atomic_load(&r->board.clips[slot])->data[0]==.4f);
  router_sound_gain(r,1,NAN);assert(atomic_load(&r->board.gain[1])==0);
  router_sound_gain(r,1,100);assert(atomic_load(&r->board.gain[1])==1);
  puts("PASS: immutable clips, finite sample/gain validation, bounded allocation");
  router_sound_play(r,slot); sb_prepare(&r->board,1,48000);
  for(int i=0;i<48001;i++) sb_sample(&r->board,1,48000,0,v);
  assert(v[0]==0 && atomic_load(&r->board.playing[1])==0);
  puts("PASS: clips finish naturally without replaying");
  // Sum a full-scale effect with a full-scale music source, then stop only effects.
  for(int i=0;i<48000*2;i++) samples[i]=1;
  int loud=router_sound_load(r,samples,48000);router_sound_play(r,loud);
  r->input[0].rate=48000;prepare(&r->ring[1][0],1);
  for(int i=0;i<4000;i++) push(&r->ring[1][0],1,1);
  router_gain(r,1,1);router_music_boost(r,0);r->smooth[1][1]=1;
  float peak=output(r,1,48000);assert(peak>0.8 && peak<=.981);
  router_sound_play(r,-1);peak=output(r,1,48000);assert(peak>0.8 && peak<=.981);
  puts("PASS: combined music/effects protected; Stop sounds leaves music playing");
  router_destroy(r);
  r=router_create();
  for(int i=0;i<48000*2;i++) samples[i]=.1;
  slot=router_sound_load(r,samples,48000);router_sound_play(r,slot);
  router_sound_gain(r,1,1);r->board.player[1].gain=1;
  for(int src=0;src<2;src++) {
    r->input[src].rate=48000;prepare(&r->ring[1][src],1);
    for(int i=0;i<5000;i++) push(&r->ring[1][src],.2,.2);
  }
  router_gain(r,1,.5);r->smooth[1][1]=.5;
  router_gain(r,2,.5);r->smooth[1][2]=.5;
  assert(fabs(output(r,1,48000)-.25)<.001);
  router_gain(r,1,1);router_music_boost(r,1);
  for(int i=0;i<12;i++) boosted=output(r,1,48000);
  assert(fabs(boosted-.7)<.003);
  router_gain(r,1,0);assert(fabs(output(r,1,48000)-.1)<.001);
  puts("PASS: one gain scales music and effects together; Max and shared mute preserve microphone contribution");
  router_destroy(r);free(samples);
  puts("Soundboard DSP tests passed; no hardware opened.");
}
