#ifndef MFL_DAMPING_H
#define MFL_DAMPING_H
#include <math.h>
/* Settings: stiffness, low compression, high compression, low rebound,
   high rebound, compression crossover, rebound crossover (game units/s). */
static float shaft_velocity(float current, float previous, double dt) {
    if (!isfinite(current)||!isfinite(previous)||!isfinite(dt)||dt<=0||dt>0.25) return 0;
    float v=(float)((current-previous)/dt);
    return isfinite(v)?v:0;
}
/* Joint position is radians; minimal angle delta avoids +/-pi wrap spikes. */
static float angular_velocity(float angle,float previous,double dt) {
    float delta=remainderf(angle-previous,6.283185307179586f);
    return shaft_velocity(delta,0,dt);
}
static int torsion_winding(float angle,float targetDegrees,float speed,int previous) {
    float error=remainderf(angle-targetDegrees*0.0174532925199433f,6.283185307179586f);
    float direction=error*speed;
    if(direction>0.00001f)return 1;
    if(direction< -0.00001f)return 0;
    return previous;
}
static float shaft_damping(const float v[7],float speed,int compression) {
    float threshold=v[compression?5:6];
    float blend=(fabsf(speed)/threshold-0.9f)/0.2f;
    blend=fmaxf(0,fminf(1,blend));
    int low=compression?1:3;
    return v[low]+blend*(v[low+1]-v[low]);
}
#endif
