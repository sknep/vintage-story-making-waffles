using System.Collections.Generic;
using HarmonyLib;
using MakingWaffles.Systems.Griddling;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace MakingWaffles;

public class MakingWafflesModSystem : ModSystem
{
    const string GriddlingChannelName = "makingwaffles.griddling";
    IServerNetworkChannel? serverChannel;

    public override void Start(ICoreAPI api)
    {
        base.Start(api);

        var harmony = new Harmony("makingwaffles.griddling");
        harmony.PatchAll();

            api.RegisterBlockClass("BlockGriddlingContainer", typeof(Systems.Griddling.BlockGriddlingContainer));
            api.RegisterBlockEntityClass("GriddleContainer", typeof(Systems.Griddling.BlockEntityGriddleContainer));
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        base.StartServerSide(api);

        serverChannel = api.Network.RegisterChannel(GriddlingChannelName)
            .RegisterMessageType<GriddlingRecipesPacket>();

        api.Event.PlayerJoin += player => SendRecipes(api, player);
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        base.StartClientSide(api);

        api.Network.RegisterChannel(GriddlingChannelName)
            .RegisterMessageType<GriddlingRecipesPacket>()
            .SetMessageHandler<GriddlingRecipesPacket>(packet => OnRecipesReceived(api, packet));
    }

    void SendRecipes(ICoreServerAPI api, IServerPlayer player)
    {
        if (serverChannel == null) return;

        GriddlingRecipeRegistrySystem? registry = api.ModLoader.GetModSystem<GriddlingRecipeRegistrySystem>();
        if (registry == null) return;

        byte[] data = GriddlingRecipeSync.Serialize(registry.GriddlingRecipes);
        serverChannel.SendPacket(new GriddlingRecipesPacket { Data = data }, player);
    }

    static void OnRecipesReceived(ICoreClientAPI api, GriddlingRecipesPacket packet)
    {
        GriddlingRecipeRegistrySystem? registry = api.ModLoader.GetModSystem<GriddlingRecipeRegistrySystem>();
        if (registry == null) return;

        List<CookingRecipe> recipes = GriddlingRecipeSync.Deserialize(api, packet.Data);
        registry.ReplaceRecipes(recipes);
    }
}
