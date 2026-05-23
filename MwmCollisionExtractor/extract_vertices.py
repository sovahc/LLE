#!/usr/bin/env python3
"""Remove collinear intermediate vertices from CubeTopologyVertices.cs"""

import re
import math

EPS = 1e-6

def parse_vector3(text):
    m = re.match(r'new\s+Vector3\(\s*([^,]+)\s*,\s*([^,]+)\s*,\s*([^)]+)\s*\)', text.strip())
    if not m:
        return None
    def pv(s):
        s = s.strip()
        return float(s.replace('f', '').replace('F', ''))
    return (pv(m.group(1)), pv(m.group(2)), pv(m.group(3)))

def cross(a, b):
    return (a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0])

def dot(a, b):
    return a[0]*b[0] + a[1]*b[1] + a[2]*b[2]

def vsub(a, b):
    return (a[0]-b[0], a[1]-b[1], a[2]-b[2])

def vlen(v):
    return math.sqrt(v[0]*v[0] + v[1]*v[1] + v[2]*v[2])

def is_between(a, b, c):
    """True if b lies on segment a-c (collinear and between, inclusive)."""
    ab = vsub(b, a)
    ac = vsub(c, a)
    if vlen(cross(ab, ac)) > EPS:
        return False
    dac = dot(ac, ac)
    if dac < EPS:
        return False
    dab = dot(ab, ac)
    return -EPS <= dab <= dac + EPS

def deduplicate(vertices):
    """Remove exact duplicate vertices."""
    seen = []
    result = []
    for v in vertices:
        dup = False
        for s in seen:
            if vlen(vsub(v, s)) < EPS:
                dup = True
                break
        if not dup:
            seen.append(v)
            result.append(v)
    return result

def remove_collinear(vertices):
    """Two-pass: mark all collinear intermediates, then remove."""
    n = len(vertices)
    if n <= 2:
        return list(vertices)

    # Pass 1: mark
    remove = [False] * n
    for j in range(n):
        for i in range(n):
            if i == j:
                continue
            for k in range(n):
                if k == i or k == j:
                    continue
                if is_between(vertices[i], vertices[j], vertices[k]):
                    remove[j] = True
                    break
            if remove[j]:
                break

    # Pass 2: remove
    return [v for i, v in enumerate(vertices) if not remove[i]]

def fmt(v):
    if v == int(v):
        return str(int(v))
    return f"{v}f"

def fmt_vec(v):
    return f"new Vector3({fmt(v[0]):>6}, {fmt(v[1]):>6}, {fmt(v[2]):>6})"

def main():
    path = "/home/cat/Projects/LLE/MwmCollisionExtractor/CubeTopologyVertices.cs"
    with open(path) as f:
        content = f.read()

    # Extract all cases
    case_re = re.compile(
        r'(CubeTopology\.\w+(?:\s+or\s+CubeTopology\.\w+)*)\s*=>\s*\[(.*?)\]',
        re.DOTALL
    )

    cases = []
    for m in case_re.finditer(content):
        name = m.group(1)
        block = m.group(2)
        vertices = []
        for vm in re.finditer(r'new\s+Vector3\([^)]+\)', block):
            v = parse_vector3(vm.group())
            if v is not None:
                vertices.append(v)
        cases.append((name, vertices))

    # Process each case
    processed = []
    for name, vertices in cases:
        original = len(vertices)
        deduped = deduplicate(vertices)
        dedup_removed = original - len(deduped)
        cleaned = remove_collinear(deduped)
        cleaned.sort(key=lambda v: (v[0], v[1], v[2]))
        collinear_removed = len(deduped) - len(cleaned)
        if dedup_removed > 0 or collinear_removed > 0:
            print(f"{name}: {original} -> {len(cleaned)} "
                  f"(dedup -{dedup_removed}, collinear -{collinear_removed})")
        processed.append((name, cleaned))

    # Rebuild the file
    lines = []
    lines.append("using System;")
    lines.append("using VRageMath;")
    lines.append("")
    lines.append("namespace MwmCollisionExtractor")
    lines.append("{")
    lines.append("\tpublic enum CubeTopology")
    lines.append("\t{")
    lines.append("\t\tStandaloneBox, Box,")
    lines.append("\t\tSlope, RotatedSlope, RoundSlope,")
    lines.append("\t\tCorner, RotatedCorner, RoundCorner, InvCorner, RoundInvCorner, RoundedSlope, Slope2Base, Slope2Tip,")
    lines.append("\t\tCorner2Base, Corner2Tip, InvCorner2Base, InvCorner2Tip,")
    lines.append("\t\tHalfBox, HalfSlopeBox, HalfSlopeInverted, HalfSlopeCorner, HalfSlopeCornerInverted,")
    lines.append("\t\tSlopedCornerTip, SlopedCornerBase, SlopedCorner, HalfSlopedCornerBase, HalfCorner,")
    lines.append("\t\tCornerSquare, CornerSquareInverted, HalfSlopedCorner, RaisedSlopedCorner,")
    lines.append("\t\tSlopeTransition, SlopeTransitionBase, SlopeTransitionBaseMirrored, SlopeTransitionMirrored,")
    lines.append("\t\tSlopeTransitionTip, SlopeTransitionTipMirrored,")
    lines.append("\t\tSquareSlopedCornerBase, SquareSlopedCornerTip, SquareSlopedCornerTipInv")
    lines.append("\t}")
    lines.append("")
    lines.append("\tpublic static class CubeTopologyVertices")
    lines.append("\t{")
    lines.append("\t\tpublic static Vector3[] GetVertices(CubeTopology topology)")
    lines.append("\t\t{")
    lines.append("\t\t\treturn topology switch")
    lines.append("\t\t\t{")

    for name, vertices in processed:
        if not vertices:
            lines.append(f"\t\t\t\t{name} => [],")
            continue
        lines.append(f"\t\t\t\t{name} => [")
        for i, v in enumerate(vertices):
            comma = "," if i < len(vertices) - 1 else ""
            lines.append(f"\t\t\t\t\t{fmt_vec(v)}{comma}")
        lines.append("\t\t\t\t\t],")

    lines.append("\t\t\t\t_ => [],")
    lines.append("\t\t\t};")
    lines.append("\t\t}")
    lines.append("\t}")
    lines.append("}")

    print()
    print("--- Output ---")
    print("\n".join(lines))

if __name__ == '__main__':
    main()
