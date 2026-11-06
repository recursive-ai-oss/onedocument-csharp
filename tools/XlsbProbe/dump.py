import sys, zipfile, collections
def recs(b):
    i=0;n=len(b)
    while i<n:
        s=i
        t=b[i];i+=1
        if t&0x80:
            t=(t&0x7f)|((b[i]&0x7f)<<7);i+=1
        sz=0
        for k in range(4):
            c=b[i];i+=1
            sz|=(c&0x7f)<<(7*k)
            if not c&0x80:break
        yield s,t,b[i:i+sz]
        i+=sz
if __name__=='__main__':
    z=zipfile.ZipFile(sys.argv[1])
    for n in z.namelist():
        print(n, z.getinfo(n).file_size)
    part=sys.argv[2] if len(sys.argv)>2 else None
    if part:
        b=z.read(part)
        lim=int(sys.argv[3]) if len(sys.argv)>3 else 60
        for k,(s,t,d) in enumerate(recs(b)):
            if k>=lim:break
            print(s,t,len(d),d[:40].hex())
