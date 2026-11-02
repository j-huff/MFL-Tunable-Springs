import struct
from mfl_metadata import root
b=(root/'GameAssembly.dll').read_bytes();pe=struct.unpack_from('<I',b,60)[0];n=struct.unpack_from('<H',b,pe+6)[0];opt=struct.unpack_from('<H',b,pe+20)[0];base=struct.unpack_from('<Q',b,pe+48)[0];sects=[]
for i in range(n):
 p=pe+24+opt+40*i;vs,va,sz,off=struct.unpack_from('<4I',b,p+8);sects.append((off,sz,va))
def addr(off):
 for o,s,v in sects:
  if o<=off<o+s:return base+v+off-o
 raise ValueError(off)
def offset(a):
 for o,s,v in sects:
  if base+v<=a<base+v+s:return a-base-v+o
 raise ValueError(a)
def u64(a):return struct.unpack_from('<Q',b,offset(a))[0]
if __name__=='__main__':
 for token in range(0x5f7,0x601): print(hex(token),hex(u64(0x1823e0a10+8*(token-1))))
