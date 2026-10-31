"""Canonical Windows evidence paths, validated independently of the Python runner OS."""
from pathlib import PureWindowsPath
import re
from baseline_contracts import require

def parts_valid(parts):
    for part in parts:
        require(part not in ('','.','..') and not part.endswith(('.', ' ')) and not any(c in part for c in ':<>"|?*') and all(ord(c)>=32 for c in part),'Unsafe Windows path component/ADS/trailing alias')
        stem=part.split('.',1)[0].upper()
        require(stem not in {'CON','PRN','AUX','NUL'} and not (len(stem)==4 and stem[:3] in ('COM','LPT') and stem[3].isdigit()),'Reserved Windows path component')

def relative(value):
    require(type(value) is str and value and '\\' not in value and all(value.split('/')),'Canonical relative evidence path must use nonempty forward-slash components')
    p=PureWindowsPath(value);require(not p.drive and not p.root,'Drive-relative/absolute evidence paths are not permitted');parts_valid(p.parts);return p

def absolute(value):
    require(type(value) is str,'Absolute launch path must be text');p=PureWindowsPath(value)
    require(p.is_absolute() and re.fullmatch('[A-Za-z]:',p.drive) is not None and p.root=='\\','Launch outputs require an explicit local Windows drive/root')
    parts_valid(p.parts[1:]);return p

def key(path):return path.as_posix().casefold()
