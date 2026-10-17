#!/usr/bin/env python3
"""Fail if the application graph reaches legacy code or violates its layer boundaries."""
from pathlib import Path
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parent.parent
allowed = {
    'Simulation': set(), 'Contracts': set(),
    'Persistence': {'Simulation'}, 'Content': {'Simulation'},
    'Session': {'Simulation', 'Contracts', 'Persistence'},
    'Plugins.ManualController': {'Simulation'},
    'Server': {'Session', 'Contracts', 'Content', 'Plugins.ManualController'},
}
solution = ET.parse(root / 'FreeTrain.slnx').getroot()
projects = {(root / p.attrib['Path']).resolve() for p in solution.iter('Project')}
expected = {root / f'src/FreeTrain.{name}/FreeTrain.{name}.csproj' for name in allowed}
expected.add(root / 'tests/FreeTrain.Tests/FreeTrain.Tests.csproj')
assert projects == expected, 'Solution has missing or unexpected projects'
for name, dependencies in allowed.items():
    path = root / f'src/FreeTrain.{name}/FreeTrain.{name}.csproj'
    project = ET.parse(path).getroot()
    refs = {(path.parent / node.attrib['Include']).resolve() for node in project.iter('ProjectReference')}
    permitted = {root / f'src/FreeTrain.{dep}/FreeTrain.{dep}.csproj' for dep in dependencies}
    assert refs <= permitted, f'{name}: forbidden project dependency {refs - permitted}'
    assert not list(project.iter('Reference')), f'{name}: raw assembly references are not allowed'
    assert not list(project.iter('COMReference')), f'{name}: COM references are not allowed'
    assert not list(project.iter('PackageReference')), f'{name}: review new package dependencies explicitly'
    if name != 'Server':
        assert project.attrib['Sdk'] == 'Microsoft.NET.Sdk', f'{name}: server/UI SDK in portable slice'
    for source in path.parent.rglob('*.cs'):
        if {'bin', 'obj'} & set(source.relative_to(path.parent).parts): continue
        text = source.read_text()
        forbidden = ['System.Windows.Forms', 'System.Drawing', 'Microsoft.DirectX', 'MagicLibrary', 'DllImport(']
        if name != 'Server': forbidden.append('Microsoft.AspNetCore')
        assert not any(token in text for token in forbidden), f'{source}: platform dependency'
print('Project boundaries: 7 backend projects and isolated test project; no legacy references.')
