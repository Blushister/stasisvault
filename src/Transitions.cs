using System.Security.Cryptography;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace CurveoStockage;

/// <summary>
/// Outils autour des piles : clé d'identité (même objet), péremption et fusion.
/// Le pourrissement reste calculé par le jeu lui-même (UpdateAndGetTransitionStates) ; on lui fournit
/// seulement, via un inventaire factice, la vitesse du pourrissement. Les autres transitions (séchage,
/// salaison, affinage…) gardent la vitesse normale.
/// </summary>
public static class Transitions
{
    private sealed class InventaireTaux : DummyInventory
    {
        public float TauxPerish = 1f;
        public InventaireTaux(ICoreAPI api) : base(api, 1) { }
        public override float GetTransitionSpeedMul(EnumTransitionType transType, ItemStack stack)
            => transType == EnumTransitionType.Perish ? TauxPerish : 1f;
    }

    public static bool APeremption(IWorldAccessor world, ItemStack pile)
        => pile.Collectible.GetTransitionableProperties(world, pile, null) is { Length: > 0 };

    public static double DerniereMaj(ItemStack pile)
        => (pile.Attributes["transitionstate"] as ITreeAttribute)?.GetDouble("lastUpdatedTotalHours", double.NaN) ?? double.NaN;

    /// <summary>
    /// Amène la pile à l'instant présent, avec le pourrissement à la vitesse donnée. Renvoie la pile obtenue :
    /// la même, une autre (viande devenue pourriture) ou null si tout a disparu.
    /// </summary>
    public static ItemStack? Avancer(IWorldAccessor world, ItemStack pile, float tauxPerish)
    {
        if (!APeremption(world, pile)) return pile;
        // L'emplacement doit appartenir à l'inventaire : le jeu le marque modifié quand l'objet se transforme.
        var inventaire = new InventaireTaux(world.Api) { TauxPerish = tauxPerish };
        var slot = inventaire[0];
        slot.Itemstack = pile;
        // Une transition au plus par appel : on insiste tant que l'objet change (viande → pourriture → rien).
        for (int i = 0; i < 4 && slot.Itemstack != null; i++)
        {
            var avant = slot.Itemstack.Collectible;
            slot.Itemstack.Collectible.UpdateAndGetTransitionStates(world, slot);
            if (slot.Itemstack == null || slot.Itemstack.Collectible == avant) break;
        }
        return slot.Itemstack;
    }

    /// <summary>Part de fraîcheur restante du pourrissement (1 = frais, 0 = en train de pourrir), ou null si l'objet ne pourrit pas.</summary>
    public static float? Fraicheur(IWorldAccessor world, ItemStack pile)
    {
        var props = pile.Collectible.GetTransitionableProperties(world, pile, null);
        if (props == null) return null;
        int i = Array.FindIndex(props, p => p?.Type == EnumTransitionType.Perish);
        if (i < 0 || pile.Attributes["transitionstate"] is not ITreeAttribute etat) return null;
        var frais = (etat["freshHours"] as FloatArrayAttribute)?.value;
        var fait = (etat["transitionedHours"] as FloatArrayAttribute)?.value;
        if (frais == null || fait == null || i >= frais.Length || i >= fait.Length || frais[i] <= 0) return null;
        return Math.Clamp(1 - fait[i] / frais[i], 0f, 1f);
    }

    /// <summary>Ajoute source à cible (même objet) en faisant la moyenne des états de fraîcheur, pondérée par les quantités.</summary>
    public static void Fusionner(ItemStack cible, ItemStack source)
    {
        var tc = cible.Attributes["transitionstate"] as ITreeAttribute;
        var ts = source.Attributes["transitionstate"] as ITreeAttribute;
        if (tc != null && ts != null)
        {
            double a = cible.StackSize, b = source.StackSize, pa = a / Math.Max(1, a + b), pb = 1 - pa;
            foreach (var nom in new[] { "freshHours", "transitionHours", "transitionedHours" })
            {
                if (tc[nom] is FloatArrayAttribute fa && ts[nom] is FloatArrayAttribute fb && fa.value.Length == fb.value.Length)
                    for (int i = 0; i < fa.value.Length; i++) fa.value[i] = (float)(fa.value[i] * pa + fb.value[i] * pb);
            }
            if (tc.HasAttribute("createdTotalHours") && ts.HasAttribute("createdTotalHours"))
                tc.SetDouble("createdTotalHours", tc.GetDouble("createdTotalHours") * pa + ts.GetDouble("createdTotalHours") * pb);
        }
        else if (tc == null && ts != null)
        {
            cible.Attributes["transitionstate"] = ts.Clone();
        }
        cible.StackSize += source.StackSize;
    }

    /// <summary>Clé d'identité : même objet et mêmes attributs, sans l'état de fraîcheur ni la température.</summary>
    public static string Cle(ItemStack pile)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((byte)pile.Class);
        w.Write(pile.Collectible.Code.ToString());
        EcrireCanonique(w, pile.Attributes, GlobalConstants.IgnoredStackAttributes);
        return Convert.ToHexString(SHA1.HashData(ms.ToArray()));
    }

    private static void EcrireCanonique(BinaryWriter w, ITreeAttribute arbre, string[]? ignores)
    {
        foreach (var (cle, attr) in arbre.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (ignores != null && ignores.Contains(cle)) continue;
            w.Write(cle);
            if (attr is ITreeAttribute sous) { w.Write("{"); EcrireCanonique(w, sous, null); w.Write("}"); }
            else { w.Write(attr.GetType().Name); attr.ToBytes(w); }
        }
    }

    /// <summary>Sérialise une pile par le code de l'objet (et non son identifiant numérique, qui peut changer).</summary>
    public static byte[] VersOctets(ItemStack pile)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((byte)pile.Class);
        w.Write(pile.Collectible.Code.ToString());
        w.Write(pile.StackSize);
        pile.Attributes.ToBytes(w);
        return ms.ToArray();
    }

    public static ItemStack? DepuisOctets(byte[] octets, IWorldAccessor world)
    {
        using var r = new BinaryReader(new MemoryStream(octets));
        var classe = (EnumItemClass)r.ReadByte();
        var code = new AssetLocation(r.ReadString());
        int quantite = r.ReadInt32();
        var attributs = new TreeAttribute();
        attributs.FromBytes(r);
        CollectibleObject? objet = classe == EnumItemClass.Block ? world.GetBlock(code) : world.GetItem(code);
        if (objet == null) return null;
        return new ItemStack(objet, quantite) { Attributes = attributs };
    }
}
