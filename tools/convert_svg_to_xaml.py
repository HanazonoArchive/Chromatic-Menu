import os
import glob
import re
import xml.etree.ElementTree as ET

def normalize_path_d(d):
    tokens = re.findall(r'([a-zA-Z])|([-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?)', d)
    tokens = [t[0] or t[1] for t in tokens]
    
    out = []
    current_cmd = None
    coord_idx = 0
    is_first_cmd = True
    
    for t in tokens:
        if t.isalpha():
            current_cmd = t
            coord_idx = 0
            if is_first_cmd and current_cmd == 'm':
                out.append('M')
            else:
                out.append(current_cmd)
            is_first_cmd = False
        else:
            # Check implicit commands
            if current_cmd in ('m', 'M'):
                if coord_idx == 2:
                    implicit = 'l' if current_cmd == 'm' else 'L'
                    out.append(implicit)
            out.append(t)
            coord_idx += 1
            
    return ' '.join(out)

def convert_svg_file(svg_path):
    tree = ET.parse(svg_path)
    root = tree.getroot()
    ns = '{http://www.w3.org/2000/svg}'
    
    parts = []
    for elem in root.iter():
        tag = elem.tag.replace(ns, '')
        if tag == 'path':
            d = elem.get('d')
            if d:
                parts.append(normalize_path_d(d))
        elif tag == 'line':
            x1 = elem.get('x1', '0')
            y1 = elem.get('y1', '0')
            x2 = elem.get('x2', '0')
            y2 = elem.get('y2', '0')
            parts.append(f"M {x1} {y1} L {x2} {y2}")
        elif tag == 'circle':
            cx = float(elem.get('cx', '0'))
            cy = float(elem.get('cy', '0'))
            r = float(elem.get('r', '0'))
            parts.append(f"M {cx-r},{cy} A {r},{r} 0 1 0 {cx+r},{cy} A {r},{r} 0 1 0 {cx-r},{cy}")
        elif tag == 'ellipse':
            cx = float(elem.get('cx', '0'))
            cy = float(elem.get('cy', '0'))
            rx = float(elem.get('rx', '0'))
            ry = float(elem.get('ry', '0'))
            parts.append(f"M {cx-rx},{cy} A {rx},{ry} 0 1 0 {cx+rx},{cy} A {rx},{ry} 0 1 0 {cx-rx},{cy}")
        elif tag == 'rect':
            x = float(elem.get('x', '0'))
            y = float(elem.get('y', '0'))
            w = float(elem.get('width', '0'))
            h = float(elem.get('height', '0'))
            rx_str = elem.get('rx')
            ry_str = elem.get('ry')
            if rx_str or ry_str:
                rx = float(rx_str or ry_str)
                ry = float(ry_str or rx_str)
                parts.append(f"M {x+rx},{y} H {x+w-rx} A {rx},{ry} 0 0 1 {x+w},{y+ry} V {y+h-ry} A {rx},{ry} 0 0 1 {x+w-rx},{y+h} H {x+rx} A {rx},{ry} 0 0 1 {x},{y+h-ry} V {y+ry} A {rx},{ry} 0 0 1 {x+rx},{y} Z")
            else:
                parts.append(f"M {x} {y} H {x+w} V {y+h} H {x} Z")
        elif tag == 'polyline':
            pts = elem.get('points', '').strip().replace(',', ' ').split()
            if len(pts) >= 2:
                cmd = [f"M {pts[0]} {pts[1]}"]
                for i in range(2, len(pts), 2):
                    cmd.append(f"L {pts[i]} {pts[i+1]}")
                parts.append(' '.join(cmd))
        elif tag == 'polygon':
            pts = elem.get('points', '').strip().replace(',', ' ').split()
            if len(pts) >= 2:
                cmd = [f"M {pts[0]} {pts[1]}"]
                for i in range(2, len(pts), 2):
                    cmd.append(f"L {pts[i]} {pts[i+1]}")
                cmd.append("Z")
                parts.append(' '.join(cmd))

    return ' '.join(parts)

def main():
    svg_dir = 'tools/svg'
    output_xaml = r'src\Chromatic Menu\Resources\Icons\LucideIcons.xaml'
    
    icon_files = sorted(glob.glob(os.path.join(svg_dir, '*.svg')))
    xaml_lines = [
        '<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"',
        '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">',
        '    <!-- Converted from official Lucide SVG icons. -->'
    ]
    
    geometries = {}
    for f in icon_files:
        name = os.path.splitext(os.path.basename(f))[0]
        geom = convert_svg_file(f)
        geometries[name] = geom
        xaml_lines.append(f'    <StreamGeometry x:Key="Icon.{name}">{geom}</StreamGeometry>')
        
    # Add alias for 'sliders' -> 'sliders-horizontal' if not present
    if 'sliders-horizontal' in geometries and 'sliders' not in geometries:
        xaml_lines.append(f'    <StreamGeometry x:Key="Icon.sliders">{geometries["sliders-horizontal"]}</StreamGeometry>')

    xaml_lines.append('</ResourceDictionary>')
    xaml_lines.append('')
    
    with open(output_xaml, 'w', encoding='utf-8') as out:
        out.write('\n'.join(xaml_lines))
        
    print(f"Successfully generated {output_xaml} with {len(geometries)} icons.")

if __name__ == '__main__':
    main()
