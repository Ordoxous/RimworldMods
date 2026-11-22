# BlueprintMaterials ideas

Ideas not yet planned or built. The README holds the design, the plan and the open questions; an idea moves there once it's taken on. Effort: S (an hour or two), M (about a day), L (more). "README" marks ideas the README already raises.

## First review (2026-11-22)

- **Frames too** (M). Change the material of a frame nothing has been delivered to yet. Once anything is delivered, it would need refunding.
- **Change by area** (M). A designator that changes every blueprint of one kind inside a dragged rectangle.
- **A better name and description** (S, README).

## Second pass

- **Stock in the menu** (S). Each material shows how much is on the map and how much the selected blueprints need, such as "Granite blocks (340, need 120)", greyed out when short.
- **Quick picks** (S). "Most in stock" and "Cheapest" at the top of the menu.
- **Style with material** (M, Ideology). Pick the blueprint's style in the same menu.
- **Remember the choice** (S). The chosen material becomes the default for the building's next placement, if vanilla's designator doesn't already keep it.
- **Materials on other MultiFloors levels** (S). The menu lists only materials on the blueprint's own map, so with MultiFloors a material stocked on another floor isn't offered. MultiFloors fixes the same gap in vanilla's own material menu (`Designator_Build.ProcessInput`), and Tweaks' `ResourcesAcrossLevels` makes the costs count every level.
