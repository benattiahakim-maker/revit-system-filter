using System.Collections.Generic;
using Egis.Revit.SystemFilter.Core.Models.Inventory;

namespace Egis.Revit.SystemFilter.Core.Services
{
    /// <summary>Choix sans Revit de la pièce d'un élément : espace MEP, puis pièce de la maquette, puis pièce d'un lien.</summary>
    public static class RoomChoice
    {
        public const string Space = "space";
        public const string Room = "room";
        public const string LinkedRoom = "linkedRoom";

        /// <summary>Premier candidat non null dans l'ordre space, room, linkedRoom ; null si aucun (champ omis).</summary>
        public static RoomRecord? Choose(RoomRecord? space, RoomRecord? room, RoomRecord? linkedRoom)
        {
            if (space != null) { space.Source = Space; return space; }
            if (room != null) { room.Source = Room; return room; }
            if (linkedRoom != null) { linkedRoom.Source = LinkedRoom; return linkedRoom; }
            return null;
        }

        public static string Summary(IEnumerable<NetworkElementRecord> equipments)
        {
            int with = 0, without = 0, space = 0, room = 0, linked = 0;
            foreach (var e in equipments)
            {
                if (e.Room is null) { without++; continue; }
                with++;
                if (e.Room.Source == Space) space++;
                else if (e.Room.Source == Room) room++;
                else linked++;
            }
            return $"pièces : équipements mécaniques avec pièce : {with} (space {space}, room {room}, linkedRoom {linked}) ; sans pièce : {without}";
        }
    }
}
