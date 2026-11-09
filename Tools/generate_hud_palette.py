"""Generate typed inspector palettes and colour-only overrides from authored HUD SCSS.

Run from the project root. Layout, opacity, animation and default colours stay in SCSS.
"""
from pathlib import Path
import json
import re

ROOT = Path(__file__).resolve().parents[1]
GROUPS = {
    'General': [('Normal', '#f9c469', 'Normal')],
    'Health': [('Normal', '#ffd36f', 'Normal'), ('Warning', '#ff9c4a', 'Warning'), ('Critical', '#ff5844', 'Critical'), ('Flatline', '#ff6045', 'Critical')],
    'Armor': [('Normal', '#ffd16b', 'Normal')],
    'Stamina': [('Normal', '#ffd36f', 'Normal'), ('Warning', '#ff9c4a', 'Warning'), ('Critical', '#ff5844', 'Critical'), ('Exhausted', '#ff5844', 'Critical')],
    'Ammunition': [('Normal', '#ffd280', 'Normal'), ('Low', '#ff9854', 'Warning'), ('Empty', '#ff6048', 'Critical'), ('Reloading', '#ffe19c', 'Warning')],
    'Crosshair': [('Normal', '#ffd36f', 'Normal'), ('Empty', '#ff5844', 'Critical'), ('Reloading', '#ff9c4a', 'Warning'), ('Tracking', '#ff9c4a', 'Warning')],
    'Vehicle': [('Normal', '#ffe0ab', 'Normal'), ('Turbo', '#79e9d3', 'Accent')],
    'Inventory': [('Normal', '#ffd38b', 'Normal'), ('Active', '#ffcf75', 'Normal'), ('Locked', '#ffc258', 'Warning')],
    'Interaction': [('Nearby', '#ffe3ad', 'Normal'), ('InRange', '#ffdd9c', 'Normal')],
    'Traversal': [('Normal', '#89e7d4', 'Normal'), ('Warning', '#ff8065', 'Critical')],
    'LifeState': [('Downed', '#ff7157', 'Critical'), ('Dead', '#ff7157', 'Critical')],
}
SLOTS = [g + s for g, fields in GROUPS.items() for s, _, _ in fields]

def slot_for(root, selector):
    if root == 'VBMasterHud':
        if '.stamina-readout' in selector:
            return 'Stamina' + ('Exhausted' if '.exhausted' in selector else 'Critical' if '.danger' in selector else 'Warning' if '.medium' in selector else 'Normal')
        if '.resource-warning' in selector: return 'StaminaExhausted'
        if any(x in selector for x in ['.armor-', '.scan-tick']): return 'ArmorNormal'
        if '.ecg-flatline' in selector: return 'HealthFlatline'
        if '.health-readout' in selector:
            return 'Health' + ('Critical' if '.danger' in selector else 'Warning' if '.medium' in selector else 'Normal')
        if any(x in selector for x in ['.state-', '.bleedout-', '.death-', '.spectating-', '.menu-button']): return 'LifeStateDowned'
        if any(x in selector for x in ['.readout', '.health-track', '.resource-track', '.hard-max', '.soft-max', '.current-health', '.ecg-segment']): return 'HealthNormal'
        return 'GeneralNormal'
    if root == 'VBAmmoHud':
        return 'Ammunition' + ('Low' if '.low-ammo' in selector else 'Empty' if '.empty' in selector else 'Reloading' if '.reloading' in selector else 'Normal')
    if root == 'VBCrosshairHud':
        return 'Crosshair' + ('Empty' if '.empty' in selector else 'Reloading' if '.reloading' in selector else 'Tracking' if '.tracking' in selector or '.barrel-point' in selector else 'Normal')
    if root == 'VBVehicleHud': return 'VehicleTurbo' if '.turbo' in selector else 'VehicleNormal'
    if root == 'VBWeaponSelectorHud': return 'Inventory' + ('Active' if '.active' in selector else 'Locked' if '.locked' in selector else 'Normal')
    if root == 'VBInteractionHud': return 'InteractionInRange' if any(x in selector for x in ['.in-range', '.interaction-', '.item-name', '.input-key']) else 'InteractionNearby'
    if root == 'VBTraversalHud': return 'TraversalWarning' if any(x in selector for x in ['.warning', '.failed', '.rejected']) else 'TraversalNormal'
    raise ValueError(root)

def extract(path):
    source = re.sub(r'/\*.*?\*/|//[^\n]*', '', path.read_text(encoding='utf-8-sig'), flags=re.S)
    stack = []
    output = []
    for token in re.split(r'([{};])', source):
        token = token.strip()
        if not token: continue
        if token == '{':
            branches = [x.strip() for x in pending.split(',')]
            parents = stack[-1] if stack else ['']
            stack.append([s.replace('&', p) if '&' in s else (p + ' ' + s).strip() for p in parents for s in branches])
        elif token == '}': stack.pop()
        elif token == ';':
            if ':' not in pending or not re.search(r'#[0-9a-fA-F]{6}|rgba?\(', pending): continue
            for selector in stack[-1]:
                if '@' in selector or '%' in selector: continue
                root = selector.split()[0].split('.')[0]
                slot = slot_for(root, selector)
                scoped = selector.replace('VBMasterHud', 'VBMasterHud.palette', 1) if root == 'VBMasterHud' else 'VBMasterHud.palette ' + selector
                output.append((scoped, slot, pending + ';'))
                if slot == 'LifeStateDowned':
                    output.append((scoped.replace('VBMasterHud.palette', 'VBMasterHud.palette.dead', 1), 'LifeStateDead', pending + ';'))
        else: pending = token
    return output

rules = [r for path in sorted((ROOT / 'Code/UI').glob('*Hud.razor.scss')) for r in extract(path)]
# The in-range marker inherits its dot colour in the original sheet. Give that stage its own override.
rules.append(('VBMasterHud.palette VBInteractionHud .in-range .marker-dot', 'InteractionInRange', 'background-color: #ffe3ad;'))

lines = ['// Generated by Tools/generate_hud_palette.py. Palette defaults match the authored HUD.', 'using System;', 'using Sandbox;', '', 'public enum VBHudColorSlot', '{']
lines += [f'\t{s},' for s in SLOTS] + ['\tCount', '}', '', 'public sealed class VBHudPalette', '{', '\t[Property, Title( "Use Master Colors" ), Description( "Enable to recolor all elements with the master stages. Per-element overrides remain available." )]', '\tpublic bool UseMasterColors { get; set; }', '\t[Property, InlineEditor] public VBHudMasterColors Master { get; set; } = new();']
for group in GROUPS:
    lines.append(f'\t[Property, InlineEditor] public VBHud{group}Colors {group} {{ get; set; }} = new();')
lines += ['', '\tprivate static readonly VBHudPalette Defaults = new();', '\tinternal static Color Hex( string hex ) => Color.Parse( hex ).Value;', '', '\tpublic Color GetColor( VBHudColorSlot slot ) => slot switch', '\t{']
for group, fields in GROUPS.items():
    for stage, _, master in fields:
        g = f'({group} ?? Defaults.{group})'
        lines.append(f'\t\tVBHudColorSlot.{group}{stage} => {g}.Resolve( {g}.{stage}, (Master ?? Defaults.Master).{master}, UseMasterColors ),')
lines += ['\t\t_ => Color.White', '\t};', '', '\tpublic int ColorHash()', '\t{', '\t\tvar hash = new HashCode();', '\t\tfor ( int i = 0; i < (int)VBHudColorSlot.Count; i++ ) hash.Add( GetColor( (VBHudColorSlot)i ) );', '\t\treturn hash.ToHashCode();', '\t}', '}', '', 'public sealed class VBHudMasterColors', '{']
for name, hex in [('Normal', '#ffd36f'), ('Warning', '#ff9c4a'), ('Critical', '#ff5844'), ('Accent', '#79e9d3')]:
    lines.append(f'\t[Property] public Color {name} {{ get; set; }} = VBHudPalette.Hex( "{hex}" );')
lines += ['}', '', 'public abstract class VBHudElementColors', '{', '\t[Property, Title( "Override Master" ), Description( "Use these element colors even when Use Master Colors is enabled." )]', '\tpublic bool OverrideMaster { get; set; }', '\tinternal Color Resolve( Color local, Color master, bool useMaster ) => useMaster && !OverrideMaster ? master : local;', '}']
for group, fields in GROUPS.items():
    lines += ['', f'public sealed class VBHud{group}Colors : VBHudElementColors', '{']
    lines += [f'\t[Property] public Color {stage} {{ get; set; }} = VBHudPalette.Hex( "{hex}" );' for stage, hex, _ in fields] + ['}']
(ROOT / 'Code/UI/VBHudPalette.cs').write_text('\n'.join(lines) + '\n', encoding='utf-8')

lines = ['// Generated colour-only rules. Regenerate after editing HUD SCSS: python Tools/generate_hud_palette.py', 'public sealed partial class VBHudPaletteStyles', '{', '\tprivate static readonly Rule[] Rules =', '\t[']
lines += [f'\t\tnew( {json.dumps(selector)}, VBHudColorSlot.{slot}, {json.dumps(declarations)} ),' for selector, slot, declarations in rules]
lines += ['\t];', '}']
(ROOT / 'Code/UI/VBHudPaletteStyles.Rules.cs').write_text('\n'.join(lines) + '\n', encoding='utf-8')
print(f'Generated {len(SLOTS)} editable colours and {len(rules)} colour rules.')
