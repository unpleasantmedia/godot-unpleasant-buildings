# UnpleasantBuildings

A building plugin for Godot 4.7 (.NET version). Draw a `Building` of any shape on the ground in the 3D view. It is walled up as many storeys as you set, with a level ground floor and a plinth down to the ground, however the ground slopes.

Work in progress. So far: outlines, walls, storeys, the plinth, roofs, snapping, doors and windows, wall details, and balconies and awnings.

## Features

- Outlines of any shape, drawn by clicking corners on the ground
- Corners dragged as handles; a handle in the middle of each side adds a corner
- Wall and plinth materials, storey count and storey height
- Flat (with a parapet), hipped, gabled and shed roofs, with a pitch, an overhang and a material; hipped roofs for any outline, by its straight skeleton
- The ground floor sits just above the highest ground under the building, and the plinth reaches below the lowest
- Drawn straight through the rendering server, and solid in the game only: nothing is added to the scene tree

## Requirements

- The **.NET version** of Godot 4.7
- .NET 8 SDK and C# 12
- A C# project: this plugin can't run in a project without one

## Install

The plugin must end up at exactly `addons/unpleasant_buildings`. As a submodule:

```bash
git submodule add https://github.com/unpleasantmedia/godot-unpleasant-buildings.git addons/unpleasant_buildings
```

Then, in Godot:

1. If the project has no `.csproj` file, choose **Project > Tools > C# > Create C# solution**.
2. Click **Build** (the hammer at the top right).
3. Enable **UnpleasantBuildings** under **Project > Project Settings > Plugins**.

## Connect your ground

The plugin knows no terrain itself. Without help, outlines are drawn on whatever is solid (or on a flat plane at the building's height), and the ground is taken to be at the building's own height. Tell it about your ground when your code loads:

```csharp
internal static class BuildingsHook
{
    [ModuleInitializer]
    internal static void Hook()
    {
        // Where a ray from the camera meets the ground: the scene's root, the ray's start and direction.
        BuildingsLinks.Ground = static (root, from, direction) => MyGround.Intersect(root, from, direction);
        // The ground's height under a place in the world, or null.
        BuildingsLinks.Height = static (root, at) => MyGround.HeightAt(root, at);
    }
}
```

## Use

1. Select a `BuildingCluster` (see Clusters), turn on **Draw** in the 3D view's toolbar, and click the ground: a new building starts there. (A `Building` added by hand, with no corners yet, starts drawing as soon as it is selected: **Draw** turns on.)
2. Click its corners on the ground, in order round it. Hold Shift to snap them to the 1 m grid shown at its floor while it is selected. Click its first corner, or press Enter, to finish. Escape or a right click also stops drawing.
3. Drag a corner to move it. Drag the handle in the middle of a side to add a corner there. Shift snaps these to the grid too. Ctrl-click a corner to take it away.
4. Set **Storeys**, **Storey Height**, **Plinth Height**, **Wall Material** and **Foundation Material** in the Inspector.
5. Under **Roof**, pick its type and material. Flat roofs have a **Parapet Height**; the others a **Roof Pitch** and **Roof Overhang**.
6. A gabled roof's ridge runs along one side, and a shed roof slopes down towards one: its longest, until you click **Roof Side** in the 3D view's toolbar and click another. The chosen side is shown along the top of the walls.

Walls go up to meet the roof: into gables under a gabled roof, and rising under a shed roof.

With **Snap** on (in the 3D view's toolbar), corners drawn or dragged snap within 0.5 m: onto another building's corner, onto the line of its side (along it or past its ends), or square to (or in line with) the corner's own sides and other buildings' sides. Where two of those lines cross near the mouse, the corner goes onto the crossing: lined up with a street front and square at once. The lines it snapped to are shown in green.

## Doors and windows

Pick **Window** or **Door** at the top of the **Wall Details** dock (shown while a building is selected), then click a wall: one of the usual size goes there, on the storey clicked, in 10 cm steps along the wall. Right-click or Escape stops placing.

Click a door or window to select it. Drag its edges to size it, or its middle to move it, in 10 cm steps. Its settings show in the Inspector (the building stays selected), with its distances from the start and the end of its wall and **Centre Across** and **Centre Up** at the top, to put it in the middle of its wall or storey. Ctrl-click one, or select it and press Delete, to take it away.

- Windows have a frame, a sill, glass and any number of panes across and up. With **All storeys** on, a window shows on every storey from its own up.
- Doors have a frame and a single door, double doors, or none (an open doorway), and can be glazed.
- Walls are 25 cm thick round their openings. A building with **Enterable** on has an inside (inner walls, a floor and a ceiling); without, its doors and windows are closed off at the back of the wall, dark.
- **Frame Material**, **Glass Material** and **Door Material** are under **Openings** in the Inspector; with none, the plugin's own are used.

## Balconies, awnings and porches

Pick **Balcony**, **Awning** or **Porch** in the **Wall Details** dock, then click a wall: one of the usual size goes there, on the storey clicked, in 10 cm steps along the wall. Right-click or Escape stops placing.

Click one to select it. Drag its edges to size it, the handle at its front to set how far out it reaches, or its middle to move it along and up its wall, all in 10 cm steps. Its settings show in the Inspector, with its distances from both ends of its wall and **Centre Across** at the top; only those its kind uses are shown. Ctrl-click one, or select it and press Delete, to take it away.

- Balconies have a slab and a railing round their three open sides: bars, a solid wall (of the slab's material) or glass. With **All storeys** on, a balcony shows on every storey from its own up. Its height is its slab's top above its storey's floor.
- Awnings are sloped (a canvas down and out from the wall, with a flap at its front, and sides if wanted) or flat (a thin canopy). Its height is where it meets the wall.
- **Slab Material**, **Railing Material** and **Awning Material** are under **Balconies & Awnings** in the Inspector; with none, the plugin's own are used (a striped one for awnings). Glass railings use the building's glass.
- Porches go on the ground storey only: a deck (its top level with the ground floor, or raised with its height), posts along its front up to a shed or flat roof, steps down from its front to the ground, a railing round its open sides with a gap at the steps, and a skirt closing the deck down to the ground. Its Inspector sets the roof's shape and height, the railing (on or off, its style and height), the steps (on or off, where along the front, how wide) and the skirt. **Deck Material** and **Post Material** default to the plugin's own wood; **Porch Roof Material** to the building's roof. A solid railing is of the deck's material.

## Interiors

Turn on **Enterable** to give a building an inside: the walls' inner faces, a floor on every storey and a ceiling under each floor above and under the top. **Interior Wall Material**, **Floor Material** and **Ceiling Material** are under **Interior** in the Inspector; with none, the walls' material is used.

Pick a storey in the toolbar's **Storey** list to work on it: in the editor, everything above it and the roof are left out (never in the game). **All** shows the whole building again.

With a storey picked, **Walls** draws interior walls: click on the floor where one starts and where it ends; each end starts the next wall, and right-click or Escape ends the run (again to stop drawing). Ends snap to other walls' ends, the inner corners, the outer walls' inner faces and other walls, and square to the building's sides; hold Shift to snap to the grid. Click a wall to select it: drag its ends, or set its thickness (10 cm to start) in the Inspector. Ctrl-click one, or select it and press Delete, to take it away.

### Doorways and arches

With a storey picked, turn **Doorway** or **Arch** on and click an interior wall: one goes in where the mouse is (it follows it along the walls first). A doorway has a frame round it (casing on both faces, lined inside, in the building's frame material); an arch has a round or segmental head (**Shape**, and **Rise** for a segmental one) and no frame. Click one to select it: drag its blue middle handle to slide it along the wall, its side and top handles to size it; **From**, **Width** and **Height** are in the Inspector too. Ctrl-click one, or press Delete with it selected, to take it away.

### Props inside

Things put under a building in the scene move with it. With [UnpleasantProps](https://github.com/unpleasantmedia/godot-unpleasant-props), a building can hold its props itself: set `PropsLinks.Holder` to give the building for a building selected, and `BuildingsLinks.Busy` to `PropsLinks.Carrying`, and the Props dock shows while the building is selected, props going straight under it (the building's own tools leave the view's clicks alone while one is in hand). While one of them is selected the building stays cut open at the storey picked, and what stands at or above the next storey's floor is hidden (in the editor only, and never saved hidden). `Building.Raycast(from, direction)` (or `Building.RaycastAll(root, from, direction)` for a whole scene) finds where a ray meets a building as shown, and the face's normal, though it has no collision in the editor: give it to the props plugin as its `PropsLinks.Surface` to put props on floors, walls and ceilings.

### Doors in the game

In an enterable building, every door's leaf (in an outer wall, and in a doorway with **Has Door** on) is hung in the game to swing open and shut; in the editor they are built in, shut. Each door says which way it is hung: **Hinge Right**, and **Opens Out** (an outer door) or **Opens Back** (a doorway: to its wall's back rather than its front). A pair of doors opens together.

Each leaf is a `BuildingDoor` (an `AnimatableBody3D`; call `Toggle()` to open or shut it) in a doorway node under the building, whose +Z is the doorway's front. A project with doors of its own sets `BuildingsLinks.Door` to make them instead: the plugin gives each its place (its origin on the hinge), its mesh and its box, and sets its `OpenAngle` property if it has one.

### Rooms

With a storey picked, turn **Rooms** on and click the floor between walls: the space round that place, closed off by the outer walls and the storey's interior walls, becomes a room (it follows the walls as they move). Click a room to select it; in the Inspector it has a **Name** (shown over it in the editor) and its own **Wall Material** (the faces of the walls facing into it), **Floor Material** and **Ceiling Material**, each falling back to the building's. Ctrl-click a room, or press Delete with it selected, to take it away. In the game, `Building.RoomAt(position)` gives the room a place is in.

### Stairs

With a storey picked (one with a storey above it), pick **Straight**, **L** or **U** beside **Stairs**, turn **Stairs** on, click the staircase's foot, then click the way it climbs (it squares up to the building's sides when near; Shift snaps the foot to the grid). It climbs to the next storey's floor, with a hole cut in that floor over it. An L turns a quarter at a landing, a U comes back beside itself; **Turn Left** picks the way.

Click a staircase to select it: drag its foot to move it, the handle a metre up it to turn it, the handles at its first step's sides to make it wider or narrower, the handle at the end of its first flight to make it longer or shorter (deeper or shallower steps, 15–35 cm; a staircase is never shorter than 3 m along the way it climbs, nor narrower than 1 m), and, on an L or U, the handle at its landing's far edge to move the turn. In the Inspector: **Width**, **Tread** (step depth), **Turn After** (an L's or U's steps before its landing; 0 for half), **Solid** (closed down to the floor, or open treads on stringers), **Railing** (on its sides not against a wall) and **Hole Railing** (round the hole above, but where the stairs come up). Ctrl-click one, or press Delete with it selected, to take it away. **Stair Material** and **Stair Railing Material** are under **Interior** on the building.

## Wall details

Wall details are scenes put on a building's walls: AC units, drainpipes, signs, lamps. Each scene's +Z faces out of the wall, and its X runs along it.

1. Make a `WallDetailType` for each scene, with a **Name** and a **Category** (a section of the dock).
   - **Wall Offset**: how far in front of the scene's origin its wall is (for scenes made for walls whose face isn't on the origin); it is moved back by that.
   - **Height Locked** and **Up**: for details that always sit at a height above their storey's floor (a drainpipe at 0): moved only along walls and between storeys.
   - **Width Property** and **Height Property**: for a scene whose root sizes itself by a property (a width along the wall, or a height up it, in metres); it is placed at the size it is given in the Inspector.
2. Put the types in a `WallDetailLibrary`, and set it under **Project > Project Settings > Unpleasant Buildings > Detail Library** (or give a building its own **Detail Library**).
3. Select a building: the **Wall Details** dock shows the library, a section per category. Pick a detail and click a wall to put it there, its middle where you click (its outline shown as you move), in 10 cm steps. Right-click or Escape stops.

Click a detail to select it. Drag its middle along its wall, up it, or onto another wall, in 10 cm steps. Its settings show in the Inspector, with its distances from both ends of its wall (to its edges) and **Centre Across** and **Centre Up** at the top. Ctrl-click one, or select it and press Delete, to take it away.

Details are put in as internal children: they aren't saved with the scene, and don't show in the Scene dock. In the editor their collision is kept out of physics.

Each change is one undo step. Moving the node fits the building to the ground in its new place. **Origin to Corner** (in the Inspector) puts its origin at the corner of its outline's bounds (lowest X and Z, so the whole building lies beyond it), on the ground; finishing an outline does that too. A blue handle there moves the whole building.

Textures are laid out in metres: along the walls round the building, and up from the building's origin.

## Clusters

A `BuildingCluster` holds a group of buildings (a street, a block, a town). With it, or anything in it that isn't in a building, selected, the toolbar shows **Draw** and **Snap** only: with **Draw** on, click the ground to start a new building under the cluster, its first corner there, and carry on drawing it as any building. A building shows them too, first, with its own tools after them. **Bake Square** sets how big the squares are that its buildings are baked in.

## Saving a building as a scene

**Save as Scene**, in a building's Inspector, writes it (and everything under it, its props included) to a scene file of its own, with its own copies of its doors, windows, details, balconies, walls, stairs and rooms, and puts an instance of it in its place (one step to undo). Duplicate the instance to use it again; edit the scene to change them all.

## Baking

A cluster's buildings drawn as they're built are a mesh or more each: fine to edit, slow in a big level. **Bake Buildings**, in a `BuildingCluster`'s Inspector, merges their look (walls, roofs, windows, doors' frames, balconies, insides, stairs, wall details) into a mesh per square of ground and material, saved as files in `BuildingsLinks.BakedFolder` (or `res://BakedBuildings`), under a `BakedBuildingsNode` in the cluster. In the game, the buildings' own look is hidden and the baked one drawn instead; their collision, doors (to swing) and props are their own still. After the first bake, only squares whose buildings changed are baked again. If they change after baking, the game shows them unbaked (slower) and warns you to bake again. Save the level after baking.

Baked, a building isn't built in the game at all until something that needs it solid comes within the cluster's **Collision Range** (80 m): then its collision, doors (to swing) and wall details are built, nearest first, a few a frame (any within 15 m at once); it lets go of them again a **Collision Margin** (30 m) further off. Tell the plugin what needs buildings solid, for example the player's camera, people walking and cars (without it, the camera alone):

```csharp
BuildingsLinks.Anchors = node => MyPlayerAndCharactersAndCars(node); // their places in the world
```

A door left open closes again if its building is let go of and built again.

## License

MIT
