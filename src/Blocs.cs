using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace CurveoStockage;

/// <summary>Bloc qui fait partie d'un réseau de stockage : son rôle est le premier mot de son code.</summary>
public interface IReseau
{
    string Role { get; }
}

/// <summary>Bloc qui fait partie d'un réseau de stockage (cœur, baie, terminal, stabilisateur, conduit).</summary>
public class BlockReseau : Block, IReseau
{
    /// <summary>coeur, baie, terminal, stabilisateur ou conduit (premier mot du code du bloc).</summary>
    public string Role => FirstCodePart();

    public override void OnBlockPlaced(IWorldAccessor world, BlockPos blockPos, ItemStack? byItemStack = null)
    {
        base.OnBlockPlaced(world, blockPos, byItemStack);
        StockageSystem.Revision++;
    }

    public override void OnBlockRemoved(IWorldAccessor world, BlockPos pos)
    {
        base.OnBlockRemoved(world, pos);
        StockageSystem.Revision++;
    }

    /// <summary>Ce que le joueur récupère : la variante « posable » (vide, orientée nord).</summary>
    private Block? VarianteObjet(IWorldAccessor world)
    {
        var parties = new Dictionary<string, string>();
        if (Variant.ContainsKey("etat")) parties["etat"] = "vide";
        if (Variant.ContainsKey("side")) parties["side"] = "north";
        if (Variant.ContainsKey("face")) parties["face"] = "north";
        return parties.Count == 0 ? this : world.GetBlock(CodeWithVariants(parties));
    }

    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
        => VarianteObjet(world) is Block b ? new[] { new ItemStack(b) } : base.GetDrops(world, pos, byPlayer, dropQuantityMultiplier);

    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
        => new ItemStack(VarianteObjet(world) ?? this);

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (Role == "emetteur" && byPlayer.InventoryManager.ActiveHotbarSlot?.Itemstack?.Collectible is ItemTablette)
        {
            if (world.Side == EnumAppSide.Server && byPlayer is Vintagestory.API.Server.IServerPlayer sp)
            {
                if (!world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.Use)) return true;
                ItemTablette.Relier(byPlayer.InventoryManager.ActiveHotbarSlot, blockSel.Position);
                sp.SendMessage(Vintagestory.API.Config.GlobalConstants.GeneralChatGroup,
                    Vintagestory.API.Config.Lang.GetL(sp.LanguageCode, "curveostockage:tablette-reliee"), EnumChatType.Notification);
            }
            return true;
        }
        var be = world.BlockAccessor.GetBlockEntity(blockSel.Position);
        switch (be)
        {
            case BETerminal t: return t.OnPlayerRightClick(byPlayer, blockSel);
            case BEBaie b: return b.OnPlayerRightClick(byPlayer, blockSel);
            case BEACarburant c: return c.OnPlayerRightClick(byPlayer, blockSel);
            case BEBus b: return b.OnPlayerRightClick(byPlayer, blockSel);
        }
        return base.OnBlockInteractStart(world, byPlayer, blockSel);
    }
}

/// <summary>Conduit : sa forme suit les blocs de réseau voisins (variante « connexions »).</summary>
public class BlockConduit : BlockReseau
{
    private static readonly (BlockFacing face, char lettre)[] Ordre =
    {
        (BlockFacing.NORTH, 'n'), (BlockFacing.EAST, 'e'), (BlockFacing.SOUTH, 's'),
        (BlockFacing.WEST, 'w'), (BlockFacing.UP, 'u'), (BlockFacing.DOWN, 'd'),
    };

    public override void OnBlockPlaced(IWorldAccessor world, BlockPos blockPos, ItemStack? byItemStack = null)
    {
        base.OnBlockPlaced(world, blockPos, byItemStack);
        Raccorder(world, blockPos);
    }

    public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
    {
        base.OnNeighbourBlockChange(world, pos, neibpos);
        Raccorder(world, pos);
    }

    public void Raccorder(IWorldAccessor world, BlockPos pos)
    {
        if (world.Side != EnumAppSide.Server) return;
        var lettres = new System.Text.StringBuilder();
        foreach (var (face, lettre) in Ordre)
            if (world.BlockAccessor.GetBlock(pos.AddCopy(face)) is IReseau) lettres.Append(lettre);
        var etat = lettres.Length == 0 ? "aucune" : lettres.ToString();
        if (Variant["connexions"] == etat) return;
        var nouveau = world.GetBlock(CodeWithVariant("connexions", etat));
        if (nouveau != null) world.BlockAccessor.ExchangeBlock(nouveau.Id, pos);
    }

    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
        var objet = world.GetBlock(CodeWithVariant("connexions", "aucune"));
        return objet == null ? base.GetDrops(world, pos, byPlayer, dropQuantityMultiplier) : new[] { new ItemStack(objet) };
    }

    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
        => new ItemStack(world.GetBlock(CodeWithVariant("connexions", "aucune")) ?? this);
}

/// <summary>Parcourt un réseau à partir d'un bloc et range ce qu'il trouve par rôle.</summary>
public static class Reseau
{
    public class Carte
    {
        public readonly List<BlockPos> Coeurs = new(), Baies = new(), Terminaux = new(), Stabilisateurs = new(), Ateliers = new(), BusStockage = new(), Automates = new(), Noeuds = new();
        public bool TropGrand;
    }

    public static Carte Explorer(IWorldAccessor world, BlockPos depart, int max)
    {
        var carte = new Carte();
        var vus = new HashSet<BlockPos> { depart.Copy() };
        var file = new Queue<BlockPos>();
        file.Enqueue(depart.Copy());
        int noeuds = 0;
        while (file.Count > 0)
        {
            var pos = file.Dequeue();
            if (world.BlockAccessor.GetBlock(pos) is not IReseau bloc) continue;
            if (++noeuds > max) { carte.TropGrand = true; return carte; }
            carte.Noeuds.Add(pos);
            switch (bloc.Role)
            {
                case "coeur": carte.Coeurs.Add(pos); break;
                case "baie": carte.Baies.Add(pos); break;
                case "terminal": carte.Terminaux.Add(pos); break;
                case "stabilisateur": carte.Stabilisateurs.Add(pos); break;
                case "atelier": carte.Ateliers.Add(pos); break;
                case "busstockage": carte.BusStockage.Add(pos); break;
                case "automate": carte.Automates.Add(pos); break;
            }
            foreach (var face in BlockFacing.ALLFACES)
            {
                var voisin = pos.AddCopy(face);
                if (vus.Add(voisin)) file.Enqueue(voisin);
            }
        }
        return carte;
    }

    /// <summary>Le cœur du réseau auquel appartient ce bloc, s'il y en a exactement un.</summary>
    public static BECoeur? TrouverCoeur(IWorldAccessor world, BlockPos depuis, out string? erreur)
    {
        var carte = Explorer(world, depuis, StockageSystem.Config.Reseau.BlocsMax);
        erreur = null;
        if (carte.Coeurs.Count == 0) { erreur = "curveostockage:erreur-sans-coeur"; return null; }
        if (carte.Coeurs.Count > 1) { erreur = "curveostockage:erreur-plusieurs-coeurs"; return null; }
        return world.BlockAccessor.GetBlockEntity(carte.Coeurs[0]) as BECoeur;
    }
}
