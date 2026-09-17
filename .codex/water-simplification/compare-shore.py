from PIL import Image
import math
p='Docs/ValidationEvidence/WaterShoreFlicker/'
w,h=1600,900
f=h/(2*math.tan(math.radians(60)/2))
def basis(yaw):
    y=math.radians(yaw); t=math.radians(35)
    return ((math.cos(t)*math.cos(y),math.cos(t)*math.sin(y),-math.sin(t)),(math.sin(y),-math.cos(y),0),(math.sin(t)*math.cos(y),math.sin(t)*math.sin(y),math.cos(t)))
def dot(a,b):return sum(x*y for x,y in zip(a,b))
def wet(c):return c[2]>c[0]*1.15 and c[2]>c[1]*1.05
for label in ['baseline','candidate']:
    ims=[Image.open(p+label+'-'+str(i)+'.png').convert('RGB') for i in range(4)]
    a=ims[0].load(); a0,r0,u0=basis(47)
    for index,yaw in [(1,46.8),(2,47.2),(3,47)]:
        b=ims[index].load(); a1,r1,u1=basis(yaw)
        mismatches=interior=0
        for y in range(300,550):
            for x in range(50,1550):
                ray=tuple(a0[k]+r0[k]*(x+0.5-w/2)/f+u0[k]*(h/2-y-0.5)/f for k in range(3))
                z=dot(ray,a1); xx=int(w/2+f*dot(ray,r1)/z); yy=int(h/2-f*dot(ray,u1)/z)
                if not (1<=xx<w-1 and 1<=yy<h-1):continue
                flag=wet(a[x,y])
                if flag!=wet(b[xx,yy]):
                    mismatches+=1
                    if all(wet(b[xx+dx,yy+dy])!=flag for dx,dy in [(-1,0),(1,0),(0,-1),(0,1)]):interior+=1
        print(label,index,'mask mismatches',mismatches,'beyond 1pixel edge',interior)
