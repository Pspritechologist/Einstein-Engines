using Content.Shared.Examine;
using Content.Shared.Item;
using Content.Shared.Construction.Steps;
using System.Collections.Generic;
using Content.Shared.Tag;
using Robust.Shared.Prototypes;

namespace Content.Shared.SimpleStation14.Construction.Steps;

[DataDefinition]
public sealed partial class FilterConstructionGraphStep : ArbitraryInsertConstructionGraphStep
{
    [DataField] public ProtoId<ItemSizePrototype> MaxSize { get; private set; }
    [DataField] public List<string> CompBlacklist { get; private set; } = new();
    [DataField] public List<ProtoId<TagPrototype>> TagBlacklist { get; private set; } = new();

    private SharedItemSystem? _itemSys;

    private SharedItemSystem GetItemSys(IEntityManager entityManager)
        => _itemSys ??= entityManager.EntitySysManager.GetEntitySystem<SharedItemSystem>();

    public override bool EntityValid(EntityUid uid, IEntityManager entityManager, IComponentFactory compFactory)
    {
        if (!entityManager.TryGetComponent<ItemComponent>(uid, out var itemComp)
            || GetItemSys(entityManager).GetSizePrototype(itemComp.Size) > GetItemSys(entityManager).GetSizePrototype(MaxSize))
            return false;

        foreach (var component in CompBlacklist)
        {
            if (entityManager.HasComponent(uid, compFactory.GetComponent(component).GetType()))
                return false;
        }

        var tagSystem = entityManager.EntitySysManager.GetEntitySystem<TagSystem>();

        if (tagSystem.HasAnyTag(uid, TagBlacklist))
            return false;

        return true;
    }

    public override void DoExamine(ExaminedEvent examinedEvent) => examinedEvent.AddMarkup(string.IsNullOrEmpty(Name)
        ? Loc.GetString(
            "construction-insert-entity-below-size",
            ("size", _itemSys?.GetItemSizeLocale(MaxSize) ?? "unknown")) // I don't think this will ever occur...
        : Loc.GetString(
            "construction-insert-exact-entity",
            ("entityName", Name)));
}
