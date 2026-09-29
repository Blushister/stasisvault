using System.Text;
using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace CurveoStockage;

[ProtoContract]
public class MsgTablette
{
    /// <summary>IdPaquets.* (liste, extraire, déposer) ou Ouvrir / Fermer ci-dessous.</summary>
    [ProtoMember(1)] public int Action;
    [ProtoMember(2)] public byte[]? Data;
    public const int Ouvrir = 2100, Fermer = 2101;
}

[ProtoContract]
public class MsgSignalTablette
{
    [ProtoMember(1)] public double Distance;
    [ProtoMember(2)] public double Portee;
}

[ProtoContract]
public class MsgInfoTablette
{
    [ProtoMember(1)] public string Cle = "";
    [ProtoMember(2)] public bool Fermer;
}

/// <summary>Tablette de stockage : clic droit sur un émetteur pour la relier, clic droit ailleurs pour ouvrir le réseau à distance.</summary>
public class ItemTablette : Item
{
    public static BlockPos? Lien(ItemStack? pile)
    {
        var a = pile?.Attributes;
        if (a == null || !a.HasAttribute("lienX")) return null;
        return new BlockPos(a.GetInt("lienX"), a.GetInt("lienY"), a.GetInt("lienZ"), a.GetInt("lienDim"));
    }

    public static void Relier(ItemSlot slot, BlockPos emetteur)
    {
        var a = slot.Itemstack?.Attributes;
        if (a == null) return;
        a.SetInt("lienX", emetteur.X); a.SetInt("lienY", emetteur.Y); a.SetInt("lienZ", emetteur.Z); a.SetInt("lienDim", emetteur.dimension);
        slot.MarkDirty();
    }

    public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
    {
        if (!firstEvent) return;
        handling = EnumHandHandling.PreventDefault;
        if (api.Side != EnumAppSide.Client) return;
        var capi = (ICoreClientAPI)api;
        if (Lien(slot.Itemstack) == null) { capi.TriggerIngameError(this, "curveostockage", Lang.Get("curveostockage:tablette-non-reliee")); return; }
        StockageSystem.De(api).Tablettes?.Ouvrir();
    }

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        var lien = Lien(inSlot.Itemstack);
        dsc.AppendLine(lien == null ? Lang.Get("curveostockage:tablette-info-libre")
            : Lang.Get("curveostockage:tablette-info-liee", lien.X - world.DefaultSpawnPosition.XYZInt.X, lien.Y, lien.Z - world.DefaultSpawnPosition.XYZInt.Z));
    }
}

/// <summary>Emplacement de dépôt d'une tablette ouverte (un par joueur), même identifiant côté client et serveur.</summary>
public class InventaireDepotDistant : InventoryGeneric
{
    public InventaireDepotDistant(ICoreAPI api, string uid) : base(1, "curveostockage-depotdistant", uid, api)
    {
        BaseWeight = 10f;
        OnGetSuitability = (source, cible, fusion) => 10f + (fusion ? 3f : 1f) + (source.Inventory is InventoryBasePlayer ? 5f : 0f);
    }
}

/// <summary>Côté serveur : les tablettes ouvertes, leurs vérifications (portée, émetteur, droits) et leurs mises à jour.</summary>
public class ServeurTablettes
{
    private class Session
    {
        public BlockPos Emetteur = null!;
        public BlockPos Coeur = null!;
        public InventaireDepotDistant Depot = null!;
        public bool Absorbe;
        public int DistanceEnvoyee = -1;
    }

    private readonly ICoreServerAPI sapi;
    private readonly IServerNetworkChannel canal;
    private readonly Dictionary<string, Session> sessions = new();

    public ServeurTablettes(ICoreServerAPI sapi)
    {
        this.sapi = sapi;
        canal = sapi.Network.GetChannel(StockageSystem.Canal).SetMessageHandler<MsgTablette>(Recevoir);
        sapi.Event.RegisterGameTickListener(_ => VerifierSessions(), 1000);
        sapi.Event.PlayerDisconnect += p => Fermer(p);
    }

    private BECoeur? Controler(IServerPlayer joueur, BlockPos emetteur, out string? erreur)
    {
        erreur = null;
        var w = sapi.World;
        if (!StockageSystem.Config.SansFil.Actif) { erreur = "curveostockage:desactive"; return null; }
        if (!PorteTablette(joueur, emetteur)) { erreur = "curveostockage:tablette-non-reliee"; return null; }
        if (w.BlockAccessor.GetChunkAtBlockPos(emetteur) == null) { erreur = "curveostockage:tablette-endormi"; return null; }
        if (w.BlockAccessor.GetBlock(emetteur) is not BlockReseau { Role: "emetteur" }) { erreur = "curveostockage:tablette-emetteur-absent"; return null; }
        if (joueur.Entity.Pos.Dimension != emetteur.dimension
            || (StockageSystem.Config.SansFil.Portee > 0 && joueur.Entity.Pos.XYZ.DistanceTo(emetteur.ToVec3d().Add(0.5, 0.5, 0.5)) > StockageSystem.Config.SansFil.Portee))
        { erreur = "curveostockage:tablette-hors-portee"; return null; }
        if (!w.Claims.TryAccess(joueur, emetteur, EnumBlockAccessFlags.Use)) { erreur = "curveostockage:tablette-interdit"; return null; }
        return Reseau.TrouverCoeur(w, emetteur, out erreur);
    }

    /// <summary>Le joueur a sur lui (barre ou sac) une tablette reliée à cet émetteur.</summary>
    private static bool PorteTablette(IServerPlayer joueur, BlockPos emetteur)
    {
        foreach (var nom in new[] { GlobalConstants.hotBarInvClassName, GlobalConstants.backpackInvClassName })
        {
            var inv = joueur.InventoryManager.GetOwnInventory(nom);
            if (inv == null) continue;
            foreach (var slot in inv)
                if (slot.Itemstack?.Collectible is ItemTablette && ItemTablette.Lien(slot.Itemstack) is BlockPos lien && lien.Equals(emetteur)) return true;
        }
        return false;
    }

    private void Recevoir(IServerPlayer joueur, MsgTablette msg)
    {
        if (msg.Action == MsgTablette.Fermer) { Fermer(joueur); return; }
        if (msg.Action == MsgTablette.Ouvrir)
        {
            Fermer(joueur);
            var lien = ItemTablette.Lien(joueur.InventoryManager.ActiveHotbarSlot?.Itemstack);
            if (lien == null) { Info(joueur, "curveostockage:tablette-non-reliee", true); return; }
            var coeurOuvert = Controler(joueur, lien, out var err);
            if (coeurOuvert == null) { Info(joueur, err ?? "curveostockage:erreur-sans-coeur", true); return; }
            var session = new Session { Emetteur = lien, Coeur = coeurOuvert.Pos.Copy(), Depot = new InventaireDepotDistant(sapi, joueur.PlayerUID) };
            session.Depot.SlotModified += _ => Absorber(joueur, session);
            sessions[joueur.PlayerUID] = session;
            joueur.InventoryManager.OpenInventory(session.Depot);
            Envoyer(joueur, coeurOuvert.Lister());
            EnvoyerSignal(joueur, session);
            return;
        }
        if (msg.Action == IdPaquets.ViderAtelier)
        {
            // Même session fermée (hors de portée…), la grille est rendue : au réseau si possible, sinon au joueur
            var cible = sessions.TryGetValue(joueur.PlayerUID, out var sv) ? Controler(joueur, sv.Emetteur, out _) : null;
            StockageSystem.De(sapi).Atelier?.Vider(cible, joueur, msg.Data?.Length > 0 && msg.Data[0] == 1);
            if (cible != null) Envoyer(joueur, cible.Lister());
            return;
        }
        if (!sessions.TryGetValue(joueur.PlayerUID, out var s)) return;
        var coeur = Controler(joueur, s.Emetteur, out var erreur);
        if (coeur == null) { Info(joueur, erreur ?? "curveostockage:erreur-sans-coeur", true); Fermer(joueur); return; }
        switch (msg.Action)
        {
            case IdPaquets.DemandeListe:
                Envoyer(joueur, coeur.Lister());
                break;
            case IdPaquets.Epingler:
                Operations.Epingler(sapi, joueur, msg.Data);
                Envoyer(joueur, coeur.Lister());
                break;
            case IdPaquets.DeposerSouris:
                if (Operations.DeposerSouris(coeur, joueur) is string refus) Info(joueur, refus, false);
                break;
            case IdPaquets.Extraire:
                Operations.Extraire(coeur, joueur, PaquetExtraire.Lire(msg.Data));
                break;
            case IdPaquets.Perforer:
                if (PaquetRemplir.Lire(msg.Data) is PaquetRemplir pp) Fabrication.Informer(joueur, Fabrication.Perforer(coeur, joueur, pp), null, t => Info(joueur, t, false));
                break;
            case IdPaquets.Commander:
                if (PaquetCommande.Lire(msg.Data) is PaquetCommande pc)
                {
                    var refusCmd = Fabrication.Commander(coeur, joueur, pc, out var detail);
                    Fabrication.Informer(joueur, refusCmd ?? "curveostockage:commande-lancee", detail, t => Info(joueur, t, false));
                    Envoyer(joueur, coeur.Lister());
                }
                break;
            case IdPaquets.RemplirAtelier:
                if (PaquetRemplir.Lire(msg.Data) is PaquetRemplir pr && StockageSystem.De(sapi).Atelier?.RemplirRecette(coeur, joueur, pr) is string m) Info(joueur, m, false);
                break;
        }
    }

    private void Absorber(IServerPlayer joueur, Session s)
    {
        var slot = s.Depot[0];
        if (s.Absorbe || slot.Empty) return;
        s.Absorbe = true;
        try
        {
            var coeur = Controler(joueur, s.Emetteur, out var erreur);
            if (coeur == null) { Info(joueur, erreur ?? "curveostockage:erreur-sans-coeur", false); return; }
            coeur.Inserer(slot.Itemstack, out var refus, joueur.PlayerName);
            if (slot.Itemstack.StackSize <= 0) slot.Itemstack = null;
            slot.MarkDirty();
            if (refus != null) Info(joueur, refus, false);
        }
        finally { s.Absorbe = false; }
    }

    /// <summary>Envoie la liste à jour aux tablettes ouvertes sur ce réseau.</summary>
    public void Diffuser(BlockPos coeur, Func<PaquetListe> liste)
    {
        PaquetListe? paquet = null;
        foreach (var (uid, s) in sessions)
        {
            if (s.Coeur != coeur || sapi.World.PlayerByUid(uid) is not IServerPlayer joueur) continue;
            paquet ??= liste();
            Envoyer(joueur, paquet);
        }
    }

    private void VerifierSessions()
    {
        foreach (var uid in sessions.Keys.ToList())
        {
            var joueur = sapi.World.PlayerByUid(uid) as IServerPlayer;
            if (joueur == null || joueur.ConnectionState != EnumClientState.Playing)
            {
                if (sessions.Remove(uid, out var perdue) && perdue.Depot[0].Itemstack is ItemStack reste && joueur?.Entity != null)
                {
                    perdue.Depot[0].Itemstack = null;
                    sapi.World.SpawnItemEntity(reste, joueur.Entity.Pos.XYZ);
                }
                continue;
            }
            var s = sessions[uid];
            if (Controler(joueur, s.Emetteur, out var erreur) == null)
            {
                Info(joueur, erreur ?? "curveostockage:erreur-sans-coeur", true);
                Fermer(joueur);
                continue;
            }
            EnvoyerSignal(joueur, s);
        }
    }

    private void EnvoyerSignal(IServerPlayer joueur, Session s)
    {
        int distance = (int)joueur.Entity.Pos.XYZ.DistanceTo(s.Emetteur.ToVec3d().Add(0.5, 0.5, 0.5));
        if (distance == s.DistanceEnvoyee) return;
        s.DistanceEnvoyee = distance;
        canal.SendPacket(new MsgSignalTablette { Distance = distance, Portee = StockageSystem.Config.SansFil.Portee }, joueur);
    }

    private void Fermer(IServerPlayer joueur)
    {
        StockageSystem.De(sapi).Atelier?.Desactiver(joueur.PlayerUID);
        if (!sessions.Remove(joueur.PlayerUID, out var s)) return;
        var reste = s.Depot[0].Itemstack;
        if (reste != null)
        {
            s.Depot[0].Itemstack = null;
            joueur.InventoryManager.TryGiveItemstack(reste, true);
            if (reste.StackSize > 0) sapi.World.SpawnItemEntity(reste, joueur.Entity.Pos.XYZ);
        }
        joueur.InventoryManager.CloseInventory(s.Depot);
    }

    private void Envoyer(IServerPlayer joueur, PaquetListe liste)
    {
        liste.Epingles = StockageSystem.De(sapi).Epingles?.De(joueur.PlayerUID) ?? new();
        var atelier = StockageSystem.De(sapi).Atelier;
        if (liste.Atelier && sessions.TryGetValue(joueur.PlayerUID, out var s)) atelier?.Activer(joueur, s.Coeur);
        else atelier?.Desactiver(joueur.PlayerUID);
        canal.SendPacket(liste, joueur);
    }

    private void Info(IServerPlayer joueur, string cle, bool fermer) => canal.SendPacket(new MsgInfoTablette { Cle = cle, Fermer = fermer }, joueur);
}

/// <summary>Côté client : ouvre la tablette et relaie les messages du serveur.</summary>
public class ClientTablettes
{
    private readonly ICoreClientAPI capi;
    private readonly IClientNetworkChannel canal;
    private GuiTablette? dialogue;

    public ClientTablettes(ICoreClientAPI capi)
    {
        this.capi = capi;
        canal = capi.Network.GetChannel(StockageSystem.Canal)
            .SetMessageHandler<PaquetListe>(p => dialogue?.Recevoir(p))
            .SetMessageHandler<MsgSignalTablette>(m => dialogue?.Signal(m.Distance, m.Portee))
            .SetMessageHandler<MsgInfoTablette>(m =>
            {
                capi.TriggerIngameError(this, "curveostockage", Lang.Get(m.Cle));
                if (m.Fermer) dialogue?.TryClose();
            });
    }

    public void Envoyer(int action, byte[]? data = null) => canal.SendPacket(new MsgTablette { Action = action, Data = data });

    public void Ouvrir()
    {
        if (dialogue?.IsOpened() == true) { dialogue.TryClose(); return; }
        dialogue = new GuiTablette(capi, this);
        dialogue.OnClosed += () => dialogue = null;
        dialogue.TryOpen();
    }
}

public class GuiTablette : GuiDialogGeneric
{
    private readonly ClientTablettes client;
    private readonly PanneauStockage panneau;
    private readonly InventaireDepotDistant depot;

    public GuiTablette(ICoreClientAPI capi, ClientTablettes client) : base(Lang.Get("curveostockage:tablette-titre"), capi)
    {
        this.client = client;
        depot = new InventaireDepotDistant(capi, capi.World.Player.PlayerUID);
        panneau = new PanneauStockage(capi, (id, data) => client.Envoyer(id, data));
        Positions.Oublier(capi, "curveostockage-tablette");
        var dialogue = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);
        SingleComposer = panneau.Composer_("curveostockage-tablette", DialogTitle, dialogue, () => TryClose(), depot,
            p => capi.Network.SendPacketClient(p), c => SingleComposer = c, signal: true);
    }

    public void Recevoir(PaquetListe paquet) => panneau.Recevoir(paquet);
    public void Signal(double distance, double portee) => panneau.Signal(distance, portee);

    public override void OnGuiOpened()
    {
        base.OnGuiOpened();
        capi.World.Player.InventoryManager.OpenInventory(depot);
        client.Envoyer(MsgTablette.Ouvrir);
    }

    public override void OnGuiClosed()
    {
        panneau.Fermer();
        base.OnGuiClosed();
        capi.World.Player.InventoryManager.CloseInventory(depot);
        client.Envoyer(MsgTablette.Fermer);
    }

    public override bool PrefersUngrabbedMouse => true;
}
