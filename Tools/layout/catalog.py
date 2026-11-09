"""Writes the build catalog and the default farm layout into Assets/Resources/Data/game_config.json.
The same two blocks go to the server (Backend/migrations/..._farm_layout.sql) so prices are checked there.
Run: python3 -I Tools/layout/catalog.py
"""
import json, os
root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
cfg_path = os.path.join(root, 'Assets/Resources/Data/game_config.json')

def item(cat, cost, level, maxn, model, size, fit='height', sale=True, **kw):
    d = dict(cat=cat, cost=cost, level=level, max=maxn, model=model, size=size, fit=fit, sale=sale)
    d.update(kw); return d

items = {
    # fields: price grows with every field bought (cost + step * bought)
    'field':        item('field', 120, 1, 24, 'proc:field', 4.0, step=40),
    # the farm's own buildings: can be moved, not bought
    'house':        item('building', 0, 1, 1, 'Village/House_1.obj', 6.5, 'footprint', sale=False),
    'barn':         item('building', 0, 1, 1, 'Village/Stable.obj', 7.5, 'footprint', sale=False),
    'coop':         item('building', 0, 1, 1, 'proc:coop', 7.0, sale=False),
    'silo':         item('building', 0, 1, 1, 'proc:silo', 2.6, sale=False),
    'pen':          item('building', 0, 1, 1, 'proc:pen', 9.0, sale=False),
    # buildings to build
    'market_stand': item('building', 250, 2, 4, 'Village/MarketStand_1.obj', 3.0, 'footprint'),
    'gazebo':       item('building', 400, 3, 3, 'Village/Gazebo.obj', 4.5, 'footprint'),
    'house_2':      item('building', 900, 4, 2, 'Village/House_2.obj', 6.5, 'footprint'),
    'house_3':      item('building', 1200, 6, 2, 'Village/House_3.obj', 6.5, 'footprint'),
    'bell_tower':   item('building', 2000, 8, 1, 'Village/Bell_Tower.obj', 4.0, 'footprint'),
    # fences: one 2 m piece each
    'fence_wood':   item('fence', 12, 1, 200, 'Village/Fence.obj', 2.0, 'length'),
    'fence_white':  item('fence', 18, 2, 200, 'proc:fence_white', 2.0, 'length'),
    'hedge':        item('fence', 15, 3, 200, 'proc:hedge', 2.0, 'length'),
    # trees and plants
    'tree_1':       item('tree', 40, 1, 40, 'Nature/CommonTree_1', 5.0),
    'tree_2':       item('tree', 40, 1, 40, 'Nature/CommonTree_2', 5.0),
    'tree_3':       item('tree', 45, 2, 40, 'Nature/CommonTree_3', 5.5),
    'tree_4':       item('tree', 45, 2, 40, 'Nature/CommonTree_4', 5.5),
    'pine':         item('tree', 50, 3, 40, 'Nature/Pine_1', 6.5),
    'bush':         item('tree', 15, 1, 60, 'Nature/Bush_Common', 1.1),
    'bush_flowers': item('tree', 25, 2, 60, 'Nature/Bush_Common_Flowers', 1.1),
    # decorations
    'flowers_red':  item('decor', 10, 1, 60, 'Nature/Flower_3_Group', 0.55),
    'flowers_yellow': item('decor', 10, 1, 60, 'Nature/Flower_4_Group', 0.55),
    'well':         item('decor', 150, 2, 3, 'Village/Well.obj', 2.2, 'footprint'),
    'cart':         item('decor', 80, 1, 4, 'Village/Cart.obj', 2.2, 'footprint'),
    'bench':        item('decor', 40, 1, 10, 'Village/Bench_1.obj', 1.6, 'footprint'),
    'bench_2':      item('decor', 50, 2, 10, 'Village/Bench_2.obj', 1.6, 'footprint'),
    'barrel':       item('decor', 15, 1, 20, 'Village/Barrel.obj', 0.9),
    'crate':        item('decor', 15, 1, 20, 'Village/Crate.obj', 0.8),
    'hay':          item('decor', 20, 1, 20, 'Village/Hay.obj', 1.0),
    'bags':         item('decor', 20, 2, 20, 'Village/Bags.obj', 1.0, 'footprint'),
    'rock':         item('decor', 30, 2, 20, 'Village/Rock_1.obj', 1.2, 'footprint'),
    'bonfire':      item('decor', 60, 3, 4, 'Village/Bonfire.obj', 1.3, 'footprint'),
    'cauldron':     item('decor', 60, 3, 4, 'Village/Cauldron.obj', 0.9, 'footprint'),
    # pets: walk around where they are placed
    'dog':          item('pet', 500, 2, 3, 'Characters/Dog', 1.0, 'length'),
    'husky':        item('pet', 600, 3, 3, 'Characters/Husky', 1.1, 'length'),
    'cat':          item('pet', 400, 2, 3, 'Characters/Cat', 0.6, 'length'),
}

# The farm as it was built before (WorldBuilder.BuildFarm), now as movable items. r = quarter turns.
D = []
def put(u, t, x, z, r=0, p=None):
    d = dict(u=u, t=t, x=x, z=z, r=r)
    if p is not None: d['p'] = p
    D.append(d)
put('d_house', 'house', -7, 9, 2); put('d_barn', 'barn', 9, 9, 2); put('d_coop', 'coop', -12.5, -6, 0)
put('d_silo', 'silo', 14, 9.5); put('d_pen', 'pen', 12.5, -6.5)
for i in range(6):
    put('d_field%d' % i, 'field', round(-4.6 + (i % 3) * 4.6, 1), round(-1.5 - (i // 3) * 4.4, 1), 0, i)
put('d_well', 'well', -2.5, 6.5); put('d_cart', 'cart', 4.2, 6.8, 1); put('d_bench', 'bench', -4.6, 6.4)
put('d_barrel', 'barrel', 5.6, 8.6); put('d_crate', 'crate', 6.2, 7.6); put('d_hay', 'hay', 9.4, -10)
for i in range(6):
    put('d_fl%d' % i, 'flowers_red' if i % 2 == 0 else 'flowers_yellow', round(-10.5 + i * 1.3, 1), 5.6)

cfg = json.load(open(cfg_path, encoding='utf-8'))
cfg['build_items'] = items
cfg['default_layout'] = D
cfg['build_area'] = {'x': -21, 'z': -22, 'w': 42, 'd': 37}
json.dump(cfg, open(cfg_path, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
print(len(items), 'items,', len(D), 'default pieces')
