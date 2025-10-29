using System.Threading.Tasks;
using Content.Shared.Maps;
using Content.Shared.Procedural;
using Content.Shared.Procedural.PostGeneration;
using Content.Shared.Storage;
using Robust.Shared.Map;

namespace Content.Server.Procedural.DungeonJob;

public sealed partial class DungeonJob
{
    /// <summary>
    /// <see cref="RoomEntranceDunGen"/>
    /// </summary>
    private async Task PostGen(RoomEntranceDunGen gen, Dungeon dungeon, HashSet<Vector2i> reservedTiles, Random random)
    {
        var setTiles = new List<(Vector2i, Tile)>();
        var tileDef = _tileDefManager[gen.Tile];
        var contents = gen.Contents; // imp remove index

        foreach (var room in dungeon.Rooms)
        {
            foreach (var entrance in room.Entrances)
            {
                if (reservedTiles.Contains(entrance))
                    continue;

                setTiles.Add((entrance, _tile.GetVariantTile((ContentTileDefinition) tileDef, random)));
            }
        }

        _maps.SetTiles(_gridUid, _grid, setTiles);

        foreach (var room in dungeon.Rooms)
        {
            foreach (var entrance in room.Entrances)
            {
                if (reservedTiles.Contains(entrance))
                    continue;

                _entManager.SpawnEntity(contents, _maps.GridTileToLocal(_gridUid, _grid, entrance)); // imp (Replaced SpawnEntitiesAttachedTo with SpawnEntity as we are giving it a set entity to spawn)
                await SuspendDungeon();

                if (!ValidateResume())
                    return;
            }
        }
    }
}
