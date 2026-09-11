from pathlib import Path

path = Path('.github/scripts/apply-042.py')
text = path.read_text(encoding='utf-8')
old = '''replace_once(
    diagnostics,
    ''' + "'''" + '''                    diskBusySamples.Add(Convert.ToDouble(item?[\"PercentDiskTime\"] ?? 0));''' + "'''" + ''',
    ''' + "'''" + '''                    diskBusySamples.Add(Math.Clamp(Convert.ToDouble(item?[\"PercentDiskTime\"] ?? 0), 0, 100));''' + "'''" + '''
)'''
new = '''replace_once(
    diagnostics,
    ''' + "'''" + '''                    if (double.IsFinite(busy)) diskBusySamples.Add(Math.Max(0d, busy));''' + "'''" + ''',
    ''' + "'''" + '''                    if (double.IsFinite(busy)) diskBusySamples.Add(Math.Clamp(busy, 0d, 100d));''' + "'''" + '''
)'''
if text.count(old) != 1:
    raise SystemExit(f'expected one stale diagnostics transform, found {text.count(old)}')
path.write_text(text.replace(old, new, 1), encoding='utf-8', newline='\n')
print('0.4.2 diagnostics transform target corrected')
