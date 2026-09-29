using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace CurveoStockage;

/// <summary>Emplacement qui n'accepte que des engrenages temporels.</summary>
public class SlotEngrenage : ItemSlot
{
    public SlotEngrenage(InventoryBase inventory) : base(inventory) { }
    private static bool EstEngrenage(ItemSlot? s) => s?.Itemstack?.Collectible.Code.ToString() == StockageSystem.Config.Carburant.Objet;
    public override bool CanHold(ItemSlot sourceSlot) => EstEngrenage(sourceSlot) && base.CanHold(sourceSlot);
    public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge)
        => EstEngrenage(sourceSlot) && base.CanTakeFrom(sourceSlot, priority);
}

/// <summary>Conteneur à carburant (stabilisateur, ancre) : les engrenages posés dans la case passent dans la réserve.</summary>
public abstract class BEACarburant : BlockEntityOpenableContainer
{
    protected readonly InventoryGeneric inventaire;
    public override InventoryBase Inventory => inventaire;
    public double Carburant;
    private bool absorbe;
    protected double carburantEnvoye = -1;
    protected static ConfigStockage Cfg => StockageSystem.Config;

    protected BEACarburant()
    {
        inventaire = new InventoryGeneric(1, null, null, (id, inv) => new SlotEngrenage(inv));
    }

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        if (api.Side == EnumAppSide.Server) inventaire.SlotModified += _ => Absorber();
    }

    /// <summary>Charges au plus, et consommation active ou non (config de l'appareil).</summary>
    public abstract double ChargeMax { get; }
    public abstract bool Consomme { get; }
    protected static double ParObjet => Math.Max(1e-6, Cfg.Carburant.ChargeParObjet);

    /// <summary>Appelé avant d'ajouter du carburant ; renvoie false pour refuser.</summary>
    protected virtual bool AvantAjout() => true;

    private void Absorber()
    {
        var slot = inventaire[0];
        if (absorbe || slot.Empty) return;
        int n = Math.Min(slot.StackSize, (int)Math.Floor((ChargeMax - Carburant) / ParObjet + 1e-9));
        if (n <= 0 || !AvantAjout()) return;
        absorbe = true;
        try
        {
            Carburant += n * ParObjet;
            slot.TakeOut(n);
            slot.MarkDirty();
            carburantEnvoye = Carburant;
            MarkDirty();
        }
        finally { absorbe = false; }
    }

    // Signature marquée obsolète : elle change en 1.23, à adapter à la sortie de cette version.
#pragma warning disable CS0618
    protected bool Autorise(IPlayer joueur)
        => new CachedAccessPerms(Api.World, Pos, joueur).IsInteractingPlayerAllowedTo(EnumBlockAccessFlags.Use, validatePickRange: true, "curveostockage");
#pragma warning restore CS0618

    public override void OnBlockBroken(IPlayer? byPlayer = null)
    {
        base.OnBlockBroken(byPlayer);
        int n = (int)Math.Floor(Carburant / ParObjet + 1e-9);
        var engrenage = Api.World.GetItem(new AssetLocation(Cfg.Carburant.Objet));
        if (Api.Side == EnumAppSide.Server && n > 0 && engrenage != null)
            Api.World.SpawnItemEntity(new ItemStack(engrenage, n), Pos.ToVec3d().Add(0.5, 0.5, 0.5));
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetDouble("carburant", Carburant);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        Carburant = tree.GetDouble("carburant");
    }
}

/// <summary>
/// Stabilisateur temporel : brûle des engrenages temporels pour ralentir le pourrissement dans le réseau.
/// Mode 0 arrêté, 1 et 2 ralentissent tous les cylindres, 3 (stase) fige les cylindres de stase.
/// </summary>
public class BEStabilisateur : BEACarburant
{
    public const int PaquetMode = 4000;
    public int Mode;
    public override string InventoryClassName => "curveostockage-stabilisateur";

    /// <summary>Arrêt, I, II, et stase seulement si les cylindres de stase sont activés.</summary>
    public static List<int> ModesDisponibles
    {
        get
        {
            var s = Cfg.Stabilisateur;
            var l = new List<int> { 0 };
            if (s.ModeI.Actif) l.Add(1);
            if (s.ModeII.Actif) l.Add(2);
            if (s.ModeStase.Actif && Cfg.Cylindres.Actif("stase")) l.Add(3);
            return l;
        }
    }
    public override double ChargeMax => Cfg.Stabilisateur.ChargeMax;
    public override bool Consomme => Cfg.Stabilisateur.Consomme;

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        // Mode désactivé depuis dans la config : arrêt
        if (api.Side == EnumAppSide.Server && !ModesDisponibles.Contains(Mode)) { Mode = 0; MarkDirty(); }
    }

    public double HeuresParEngrenage(int mode) => Cfg.Stabilisateur.Mode(mode)?.HeuresParCharge is double h && h > 0 ? h : double.PositiveInfinity;

    /// <summary>Brûle le carburant pour dtHeures de jeu ; renvoie la part du temps pendant laquelle le mode a été alimenté (0..1).</summary>
    public double Consommer(double dtHeures, bool tempete)
    {
        if (Mode == 0 || !Cfg.Stabilisateur.Actif) return 0;
        if (!Consomme) return 1;
        if (dtHeures <= 0) return Carburant > 0 ? 1 : 0;
        double besoin = dtHeures / HeuresParEngrenage(Mode) * (tempete ? Math.Max(0, Cfg.Stabilisateur.MultiplicateurTempete) : 1);
        double actif;
        if (Carburant >= besoin) { Carburant -= besoin; actif = 1; }
        else { actif = besoin > 0 ? Carburant / besoin : 1; Carburant = 0; }
        Api.World.BlockAccessor.GetChunkAtBlockPos(Pos)?.MarkModified();
        if (Math.Abs(Carburant - carburantEnvoye) >= 0.01 || (Carburant == 0 && carburantEnvoye != 0))
        {
            carburantEnvoye = Carburant;
            MarkDirty();
        }
        return actif;
    }

    /// <summary>Fait avancer les horloges du réseau à l'ancienne vitesse avant un changement de mode ou de carburant.</summary>
    private void FigerHorloges() => Reseau.TrouverCoeur(Api.World, Pos, out _)?.MiseAJour();

    protected override bool AvantAjout() { FigerHorloges(); return true; }

    public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
    {
        if (Api.Side == EnumAppSide.Client)
            toggleInventoryDialogClient(byPlayer, () => new GuiStabilisateur((ICoreClientAPI)Api, this));
        return true;
    }

    public override void OnReceivedClientPacket(IPlayer player, int packetid, byte[] data)
    {
        base.OnReceivedClientPacket(player, packetid, data);
        if (packetid < PaquetMode || !ModesDisponibles.Contains(packetid - PaquetMode) || !Autorise(player)) return;
        FigerHorloges();
        Mode = packetid - PaquetMode;
        MarkDirty();
        Reseau.TrouverCoeur(Api.World, Pos, out _)?.Journaliser(player.PlayerName, 3, null, Mode);
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetInt("mode", Mode);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        Mode = tree.GetInt("mode");
        (invDialog as GuiStabilisateur)?.Maj();
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        dsc.AppendLine(Lang.Get("curveostockage:stab-info-mode", Lang.Get("curveostockage:mode-" + Mode)));
        if (!Cfg.Stabilisateur.Actif) { dsc.AppendLine(Lang.Get("curveostockage:desactive")); return; }
        if (Consomme) dsc.AppendLine(Lang.Get("curveostockage:stab-info-carburant", Carburant.ToString("0.##"), ChargeMax));
        if (Mode > 0 && Carburant <= 0 && Consomme) dsc.AppendLine(Lang.Get("curveostockage:stab-info-vide"));
    }
}
