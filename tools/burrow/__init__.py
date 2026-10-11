"""Offline generator for the AR burrow network.

Pipeline (all metres, Unity axes: X right, Y up, Z forward; sculpture centre at the origin):
    minds.py   pick ~208 worm minds (best poet per generation per flask) -> build/burrow/minds.json
    funnel.py  gravity-well profile rooted at the sculpture, place minds on it
    graph.py   lineage chain + similarity cross-links + blind spurs + sculpture tendril
    route.py   organic paths for every edge, relaxed for clearance
    calibre.py Murray's-law radii from flow toward the sculpture
    field.py   signed distance field -> marching cubes -> decimated mesh + per-vertex spine attributes
    score.py   tunnel-ness metrics used to pick among candidate seeds
    bake.py    everything Unity needs -> Assets/StreamingAssets/burrow/
    preview.py Blender render of the bake for eyeballing
"""
