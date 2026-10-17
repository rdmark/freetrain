#!/usr/bin/env python3
"""Curated legacy content importer; stdlib only. No native graphics/runtime dependency."""
import hashlib, json, re, struct, sys, zlib
from pathlib import Path
import xml.etree.ElementTree as ET
ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'web/public/assets'
META = ROOT / 'web/src/generated/sprites.json'

def manifest(path):
    text = path.read_text(encoding='utf-8-sig')
    if len(text) > 262144 or re.search(r'\b(SYSTEM|PUBLIC)\b', text):
        raise ValueError('External/oversized XML is unsupported')
    entities = dict(re.findall(r"<!ENTITY\s+(\w+)\s+'([^']*)'\s*>", text))
    if len(entities) > 16 or any('&' in v or len(v) > 8192 for v in entities.values()):
        raise ValueError('Nested/oversized entities are unsupported')
    text = re.sub(r'<!DOCTYPE[^\[]*\[.*?\]>', '', text, flags=re.S)
    for name, value in entities.items():
        if text.count('&' + name + ';') > 64: raise ValueError('Entity expansion limit')
        text = text.replace('&' + name + ';', value)
    if len(text) > 524288: raise ValueError('Expanded XML limit')
    root = ET.fromstring(text)
    contributions = {}
    for node in root.findall('contribution'):
        key = node.get('id')
        if key in contributions: raise ValueError('Duplicate contribution: ' + key)
        contributions[key] = node
    return contributions

def resolve(folder, name):
    matches = [p for p in folder.iterdir() if p.name.casefold() == name.casefold()]
    if len(matches) != 1: raise ValueError('Missing/ambiguous asset: ' + name)
    return matches[0]

def bitmap(path):
    b = path.read_bytes()
    if b[:2] != b'BM': raise ValueError('Not BMP')
    start, = struct.unpack_from('<I', b, 10)
    width, height, planes, bits, compression = struct.unpack_from('<iiHHI', b, 18)
    if compression or planes != 1 or bits not in (4, 8, 24) or not (0 < width <= 4096 and 0 < height <= 4096):
        raise ValueError('Unsupported bitmap: ' + str(path))
    stride = ((width * bits + 31) // 32) * 4
    def pixel(x, y):
        i = start + (height - y - 1) * stride
        if bits == 24:
            blue, green, red = b[i + x*3:i + x*3 + 3]
        else:
            index = b[i + x] if bits == 8 else (b[i + x//2] >> (4 if x%2 == 0 else 0)) & 15
            blue, green, red = b[54 + index*4:57 + index*4]
        return red, green, blue
    key = pixel(0, 0)  # BitmapSurfaceLoader: upper-left color key.
    return width, height, [(*pixel(x,y), 0 if pixel(x,y) == key else 255) for y in range(height) for x in range(width)]

def png(width, height, pixels):
    def chunk(kind, data):
        return struct.pack('>I',len(data))+kind+data+struct.pack('>I',zlib.crc32(kind+data))
    raw = b''.join(b'\0'+bytes(v for p in pixels[y*width:(y+1)*width] for v in p) for y in range(height))
    return b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',width,height,8,6,0,0,0))+chunk(b'IDAT',zlib.compress(raw,9))+chunk(b'IEND',b'')

def main():
    OUT.mkdir(parents=True, exist_ok=True)
    check = '--check' in sys.argv
    def write(path, data):
        if check:
            if not path.exists() or path.read_bytes() != data: raise ValueError('Generated content drift: '+str(path))
        else: path.write_bytes(data)
    sprites = {}
    sources = []
    def crop(name, path, x, y, w, h, offset=0):
        width, height, pixels = bitmap(path)
        if x < 0 or y < 0 or x+w > width or y+h > height: raise ValueError('Crop outside image: '+name)
        rgba = [pixels[(y+j)*width+x+i] for j in range(h) for i in range(w)]
        image = name+'.png'
        write(OUT/image,png(w,h,rgba))
        # One bit per pixel, row strings avoid JS bitwise width limits.
        mask = [''.join('1' if p[3] else '0' for p in rgba[j*w:(j+1)*w]) for j in range(h)]
        lights = {(8,0,0):(255,8,8), (0,8,0):(252,243,148), (0,0,8):(255,227,99)}
        night = [(*lights.get(p[:3], tuple(v//4 for v in p[:3])),p[3]) for p in rgba]
        # Authored recolor variant: swap red/green channels; preserve alpha.
        tint = [(p[1],p[0],p[2],p[3]) for p in rgba]
        write(OUT/(name+'-night.png'),png(w,h,night))
        write(OUT/(name+'-tint.png'),png(w,h,tint))
        sprites[name] = dict(image='/assets/'+image, nightImage='/assets/'+name+'-night.png', tintImage='/assets/'+name+'-tint.png', width=w,height=h,offset=offset,mask=mask)
        source = str(path.relative_to(ROOT))
        if not any(s['path'] == source for s in sources): sources.append(dict(path=source,sha256=hashlib.sha256(path.read_bytes()).hexdigest()))
    ayase = ROOT/'plugins/org.kohsuke.freetrain.structure.ayase'
    miha = ROOT/'plugins/org.kohsuke.freetrain.structure.miha'
    a = manifest(ayase/'plugin.xml'); m = manifest(miha/'plugin.xml')
    pier = a['{B32B765C-C74C-4BD9-84C6-E75A3E6DFC27}']
    condo = m['{C7536844-7CD0-49e0-8982-2819F5792B32}']
    assert pier.findtext('size') == '1,1' and condo.findtext('minHeight') == '2' and condo.findtext('maxHeight') == '5'
    for name, node, contributions, folder in [('pier',pier,a,ayase),('condo',condo,m,miha)]:
        for part in ([node.find('sprite')] if name == 'pier' else list(node.find('pictures'))):
            picture = contributions[part.find('picture').get('ref')].find('picture')
            x,y = map(int,part.get('origin').split(',')); offset=int(part.get('offset'))
            crop(name if name=='pier' else name+'-'+part.tag,resolve(folder,picture.get('src')),x,y,32,offset+16,offset)
    train = ROOT/'plugins/org.kohsuke.freetrain.trains'
    manifest(train/'plugin.xml')
    for d, index in [('north',0),('east',4),('south',0),('west',4)]:
        crop('train-'+d,resolve(train,'train.bmp'),index*32,0,32,32,9)
    # Source effect slices, displayed only in the labeled client-side visual sample.
    crop('fence', ROOT/'core/res/ConstructionFence.bmp',0,0,32,32,16)
    crop('slope', ROOT/'core/res/ugslope.bmp',0,0,32,32,0)
    write(META, (json.dumps(dict(version=1,baseline='1f755ca',sprites=sprites,sources=sources),ensure_ascii=False,indent=2)+'\n').encode('utf-8'))
    print(f'Imported {len(sprites)} sprites from {len(sources)} source images')
if __name__ == '__main__': main()
