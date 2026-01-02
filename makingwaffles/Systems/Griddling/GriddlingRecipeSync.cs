using System;
using System.Collections.Generic;
using System.IO;
using ProtoBuf;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace MakingWaffles.Systems.Griddling
{
    [ProtoContract]
    public class GriddlingRecipesPacket
    {
        [ProtoMember(1)]
        public byte[] Data { get; set; } = Array.Empty<byte>();
    }

    public static class GriddlingRecipeSync
    {
        public static byte[] Serialize(IEnumerable<CookingRecipe> recipes)
        {
            using MemoryStream ms = new MemoryStream();
            using BinaryWriter writer = new BinaryWriter(ms);

            int count = 0;
            if (recipes is ICollection<CookingRecipe> col) count = col.Count;
            else
            {
                foreach (CookingRecipe _ in recipes) count++;
            }

            writer.Write(count);
            foreach (CookingRecipe recipe in recipes)
            {
                recipe.ToBytes(writer);
            }

            return ms.ToArray();
        }

        public static List<CookingRecipe> Deserialize(ICoreAPI api, byte[] data)
        {
            using MemoryStream ms = new MemoryStream(data);
            using BinaryReader reader = new BinaryReader(ms);

            int count = reader.ReadInt32();
            List<CookingRecipe> recipes = new List<CookingRecipe>(count);
            for (int i = 0; i < count; i++)
            {
                CookingRecipe recipe = new CookingRecipe();
                recipe.FromBytes(reader, api.World);
                recipes.Add(recipe);
            }

            return recipes;
        }
    }
}
