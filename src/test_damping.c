#include "damping.h"
#include <assert.h>
#include <stdio.h>
int main(void){
 float v[7]={400,10,30,50,90,.5,1};
 assert(fabsf(shaft_velocity(1,.99,.02)-.5f)<.00001);
 assert(fabsf(shaft_velocity(.99,1,.02)+.5f)<.00001);
 assert(shaft_velocity(1,NAN,.02)==0);
 assert(shaft_velocity(1,0,0)==0);
 assert(shaft_velocity(1,0,2)==0);
 assert(shaft_damping(v,-.1,1)==10);
 assert(shaft_damping(v,-1,1)==30);
 assert(fabsf(shaft_damping(v,-.5,1)-20)<.0001);
 assert(shaft_damping(v,.1,0)==50);
 assert(shaft_damping(v,2,0)==90);
 assert(fabsf(shaft_damping(v,1,0)-70)<.0001);
 v[1]=v[2]=123;assert(shaft_damping(v,-50,1)==123);
 assert(fabsf(angular_velocity(-3.13f,3.13f,.02)-1.15927f)<.001f);
 assert(fabsf(angular_velocity(3.13f,-3.13f,.02)+1.15927f)<.001f);
 assert(angular_velocity(1,NAN,.02)==0);
 assert(torsion_winding(.4f,0,1,0)==1);
 assert(torsion_winding(.4f,0,-1,1)==0);
 assert(torsion_winding(-.4f,0,-1,0)==1);
 assert(torsion_winding(-.4f,0,1,1)==0);
 assert(torsion_winding(2,90,1,0)==1);
 assert(torsion_winding(.4f,0,0,1)==1);
 puts("Linear/angular velocity, winding direction, wraparound, four damping regions and transitions passed");
}
