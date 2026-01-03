using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace MakingWaffles.Systems.Griddling
{
    public class GriddlingRecipeRegistrySystem : ModSystem
    {
        public List<CookingRecipe> GriddlingRecipes { get; } = new List<CookingRecipe>();

        public override double ExecuteOrder() => 0.6;

        public override void AssetsLoaded(ICoreAPI api)
        {
            if (api is not ICoreServerAPI sapi) return;

            // not a typo: "grriddling" is correct here. The game eagerly matches "recipes/grid" so 
            // any new system/recipe name may not start with a substring of another system. Grrrr.
            Dictionary<AssetLocation, JToken> recipes = sapi.Assets.GetMany<JToken>(sapi.Server.Logger, "recipes/grriddling");

            foreach (var val in recipes)
            {
                if (val.Value is JObject)
                {
                    LoadRecipe(sapi, val.Key, val.Value);
                }
                else if (val.Value is JArray)
                {
                    foreach (var token in (JArray)val.Value)
                    {
                        LoadRecipe(sapi, val.Key, token);
                    }
                }
            }

            sapi.World.Logger.Event("{0} griddling recipes loaded", recipes.Count);
            sapi.World.Logger.StoryEvent(Lang.Get("makingwaffles:griddling-storyevent-loaded", "Taste and smell…"));
            
        }

        private void LoadRecipe(ICoreServerAPI sapi, AssetLocation loc, JToken jrec)
        {
            var recipe = jrec.ToObject<CookingRecipe>(loc.Domain);
            if (recipe == null || !recipe.Enabled) return;

            recipe.Resolve(sapi.World, "griddling recipe " + loc);
            GriddlingRecipes.Add(recipe);
        }

        public void ReplaceRecipes(IEnumerable<CookingRecipe> recipes)
        {
            GriddlingRecipes.Clear();
            GriddlingRecipes.AddRange(recipes);
        }
    }
}
