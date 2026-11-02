import struct,pathlib
root=pathlib.Path(__file__).resolve().parents[2]
b=(root/'Mars First Logistics_Data/il2cpp_data/Metadata/global-metadata.dat').read_bytes()
h=struct.unpack_from('<64I',b)
def string(i):
 p=h[6]+i
 return b[p:b.index(b'\0',p)].decode('utf8')
def typ(i): return struct.unpack_from('<16i8H2I',b,h[40]+i*88)
def fields(i):
 t=typ(i)
 for j in range(t[8],t[8]+t[18]):
  v=struct.unpack_from('<3I',b,h[24]+12*j)
  yield j,string(v[0]),v[1],hex(v[2])
def methods(i):
 t=typ(i)
 for j in range(t[9],t[9]+t[16]):
  v=struct.unpack_from('<7I4H',b,h[12]+36*j)
  yield j,string(v[0]),v[2],hex(v[6]),v[10]
if __name__=='__main__':
 for i in range(h[41]//88):
  t=typ(i);name=string(t[0])
  if any(x in name.lower() for x in ['spring','suspension']):
   print('TYPE',i,string(t[1]),name,'byval',t[2],'parent',t[4]);print('FIELDS',list(fields(i)));print('METHODS',list(methods(i)))
