using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace CurveoStockage;

/// <summary>Dépôt et retrait demandés par un joueur, communs au terminal posé et à la tablette.</summary>
public static class Operations
{
    /// <summary>Range ce que le joueur tient au bout de la souris. Renvoie la clé d'un refus éventuel.</summary>
    public static string? DeposerSouris(BECoeur coeur, IServerPlayer joueur)
    {
        var souris = joueur.InventoryManager.MouseItemSlot;
        if (souris == null || souris.Empty) return null;
        coeur.Inserer(souris.Itemstack, out var refus, joueur.PlayerName);
        if (souris.Itemstack.StackSize <= 0) souris.Itemstack = null;
        souris.MarkDirty();
        return refus;
    }

    public static void Epingler(ICoreAPI api, IServerPlayer joueur, byte[]? data)
    {
        if (data == null) return;
        string? cle;
        try { cle = Vintagestory.API.Util.SerializerUtil.Deserialize<string>(data); } catch { return; }
        if (string.IsNullOrEmpty(cle) || cle.Length > IdPaquets.CleMax) return;
        StockageSystem.De(api).Epingles?.Basculer(joueur.PlayerUID, cle);
    }

    /// <summary>Mode 0 : une pile dans la main ; 1 : une demi-pile ; 2 : une pile vers l'inventaire.</summary>
    public static void Extraire(BECoeur coeur, IServerPlayer joueur, PaquetExtraire? demande)
    {
        if (demande == null) return;
        var (modele, total) = coeur.Decrire(demande.Cle);
        if (modele == null || total <= 0) return;
        int pile = (int)Math.Min(total, Math.Max(1, modele.Collectible.MaxStackSize));
        int quantite = demande.Mode == 1 ? Math.Max(1, pile / 2) : pile;
        if (demande.Mode == 2)
        {
            var sortie = coeur.Extraire(demande.Cle, quantite);
            if (sortie == null) return;
            int avant = sortie.StackSize;
            joueur.InventoryManager.TryGiveItemstack(sortie, true);
            if (avant - sortie.StackSize > 0) coeur.Journaliser(joueur.PlayerName, 1, sortie, avant - sortie.StackSize);
            if (sortie.StackSize > 0) coeur.Restituer(sortie);
            return;
        }
        var souris = joueur.InventoryManager.MouseItemSlot;
        if (souris == null || !souris.Empty) return;
        var main = coeur.Extraire(demande.Cle, quantite, joueur.PlayerName);
        if (main == null) return;
        souris.Itemstack = main;
        souris.MarkDirty();
    }
}
