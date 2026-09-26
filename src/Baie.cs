using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace CurveoStockage;

/// <summary>Emplacement qui n'accepte qu'un cylindre-mémoire.</summary>
public class SlotCylindre : ItemSlot
{
    public SlotCylindre(InventoryBase inventory) : base(inventory) { MaxSlotStackSize = 1; }

    public override bool CanHold(ItemSlot sourceSlot)
        => sourceSlot?.Itemstack?.Collectible is ItemCylindre && base.CanHold(sourceSlot);

    public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge)
        => sourceSlot?.Itemstack?.Collectible is ItemCylindre && base.CanTakeFrom(sourceSlot, priority);
}

/// <summary>Baie à cylindres : 8 emplacements, s'ouvre comme un coffre.</summary>
public class BEBaie : BlockEntityOpenableContainer
{
    private readonly InventoryGeneric inventaire;
    public override InventoryBase Inventory => inventaire;
    public override string InventoryClassName => "curveostockage-baie";

    public BEBaie()
    {
        inventaire = new InventoryGeneric(8, null, null, (id, inv) => new SlotCylindre(inv));
    }

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        if (api.Side == EnumAppSide.Server) inventaire.SlotModified += _ => MajApparence();
    }

    private void MajApparence()
    {
        var etat = inventaire.Empty ? "vide" : "pleine";
        if (Block.Variant["etat"] == etat) return;
        var nouveau = Api.World.GetBlock(Block.CodeWithVariant("etat", etat));
        if (nouveau != null) Api.World.BlockAccessor.ExchangeBlock(nouveau.Id, Pos);
    }

    /// <summary>Les cylindres ne pourrissent pas : pas de « vitesse de dégradation » de coffre, seulement le nombre de cylindres.</summary>
    public override void GetBlockInfo(IPlayer forPlayer, System.Text.StringBuilder dsc)
        => dsc.AppendLine(Lang.Get("curveostockage:baie-info", Enumerable.Count(inventaire, s => !s.Empty), inventaire.Count));

    public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
    {
        if (Api.Side == EnumAppSide.Client)
        {
            toggleInventoryDialogClient(byPlayer, () => new GuiBaie((ICoreClientAPI)Api, this));
        }
        return true;
    }
}
