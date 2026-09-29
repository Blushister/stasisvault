using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace CurveoStockage;

/// <summary>Cylindre-mémoire : l'objet ne porte que son identifiant et un résumé ; le contenu est gardé par le serveur.</summary>
public class ItemCylindre : Item
{
    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        var (objetsMax, typesMax) = StockageSystem.Config.Capacite(this);
        var attr = inSlot.Itemstack?.Attributes;
        if (attr == null) return;
        if (!attr.HasAttribute("cylId"))
            dsc.AppendLine(Lang.Get("curveostockage:cyl-neuf", objetsMax, typesMax));
        else
            dsc.AppendLine(Lang.Get("curveostockage:cyl-contenu", attr.GetLong("cylObjets"), objetsMax, attr.GetInt("cylTypes"), typesMax));
        var codes = (attr["cylTopCodes"] as Vintagestory.API.Datastructures.StringArrayAttribute)?.value;
        var qtes = (attr["cylTopQte"] as Vintagestory.API.Datastructures.IntArrayAttribute)?.value;
        if (codes != null && qtes != null && codes.Length > 0)
        {
            dsc.AppendLine(Lang.Get("curveostockage:cyl-principaux"));
            for (int i = 0; i < codes.Length && i < qtes.Length; i++)
                dsc.AppendLine("  • " + (EvenementJournal.Pile(codes[i], world)?.GetName() ?? codes[i]) + " ×" + Style.Abreger(qtes[i]));
        }
        var materiau = Variant["materiau"];
        if (materiau is "refrigere" or "stase")
            dsc.AppendLine(StockageSystem.Config.Effet(materiau) == materiau
                ? Lang.Get("curveostockage:cyl-desc-" + materiau) : Lang.Get("curveostockage:cyl-desactive"));
    }
}
