using Content.Server.Power.Components;
using Robust.Shared.Audio;
using Robust.Shared.Player;
using Robust.Shared.Containers;
using Content.Server.Sound.Components;
using Content.Shared.Sound.Components;
using Content.Shared.Interaction;
using Robust.Shared.Timing;
using Robust.Shared.Audio.Systems;
using Content.Server.DeviceLinking.Systems;
using Content.Server.DeviceLinking.Events;

namespace Content.Server.SimpleStation14.LoudSpeakers;

public sealed class DoorSignalControlSystem : EntitySystem
{
    [Dependency] private readonly DeviceLinkSystem _signal = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LoudSpeakerComponent, ComponentShutdown>(OnShutdown);

        SubscribeLocalEvent<LoudSpeakerComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<LoudSpeakerComponent, SignalReceivedEvent>(OnSignalReceived);

        SubscribeLocalEvent<LoudSpeakerComponent, InteractHandEvent>(OnInteractHand);
    }

    private void OnShutdown(EntityUid uid, LoudSpeakerComponent component, ComponentShutdown args)
    {
        if (component.CurrentPlayingSound is { } soundEnt)
            _audio.Stop(soundEnt.Owner, soundEnt.Comp);
    }

    private void OnInit(EntityUid uid, LoudSpeakerComponent component, ComponentInit args)
    {
        if (component.Ports)
            _signal.EnsureSinkPorts(uid, component.PlaySoundPort);
    }

    /// <summary>
    ///     Tries to play a loudspeaker.
    /// </summary>
    /// <param name="uid">The Loudspeaker to play.</param>
    /// <param name="component">The Loudspeaker component.</param>
    /// <returns>True if the Loudspeaker was played, false otherwise.</returns>
    public bool TryPlayLoudSpeaker(Entity<LoudSpeakerComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp))
            return false;

        if (ent.Comp.NextPlayTime > _timing.CurTime)
            return false;

        if (TryComp<ApcPowerReceiverComponent>(ent, out var powerComp) && !powerComp.Powered)
            return false;

        PlayLoudSpeaker(ent!, GetSpeakerSound(ent!));

        return true;
    }

    private void PlayLoudSpeaker(Entity<LoudSpeakerComponent> ent, SoundSpecifier sound)
    {
        var newParams = sound.Params
            .WithVolume(sound.Params.Volume * ent.Comp.VolumeMod)
            .WithMaxDistance(sound.Params.MaxDistance * ent.Comp.RangeMod)
            .WithRolloffFactor(sound.Params.RolloffFactor * ent.Comp.RolloffMod)
            .WithVariation((sound.Params.Variation is { } and > 0 ? ent.Comp.DefaultVariance : sound.Params.Variation) * ent.Comp.VarianceMod);

        if (ent.Comp.Interrupt && ent.Comp.CurrentPlayingSound is { } soundEnt)
            _audio.Stop(soundEnt.Owner, soundEnt.Comp);

        ent.Comp.NextPlayTime = _timing.CurTime + ent.Comp.Cooldown;

        ent.Comp.CurrentPlayingSound = _audio.PlayEntity(sound, Filter.Pvs(ent, ent.Comp.RangeMod), ent, true, newParams);
    }

    private SoundSpecifier GetSpeakerSound(Entity<LoudSpeakerComponent> ent)
    {
        if (!_container.TryGetContainer(ent, ent.Comp.ContainerSlot, out var container))
            return ent.Comp.DefaultSound;

        if (container.ContainedEntities.Count == 0)
            return ent.Comp.DefaultSound;

        var entity = container.ContainedEntities[0];

        switch (entity)
        {
            case { } when TryComp<EmitSoundOnTriggerComponent>(entity, out var trigger) && trigger.Sound != null:
                return trigger.Sound;

            case { } when TryComp<EmitSoundOnActivateComponent>(entity, out var activate) && activate.Sound != null:
                return activate.Sound;

            case { } when TryComp<EmitSoundOnUseComponent>(entity, out var use) && use.Sound != null:
                return use.Sound;

            case { } when TryComp<EmitSoundOnDropComponent>(entity, out var drop) && drop.Sound != null:
                return drop.Sound;

            case { } when TryComp<EmitSoundOnLandComponent>(entity, out var land) && land.Sound != null:
                return land.Sound;

            default:
                return ent.Comp.DefaultSound;
        }
    }

    private void OnSignalReceived(Entity<LoudSpeakerComponent> ent, ref SignalReceivedEvent args)
    {
        if (args.Port == ent.Comp.PlaySoundPort)
            TryPlayLoudSpeaker(ent!);
    }

    private void OnInteractHand(Entity<LoudSpeakerComponent> ent, ref InteractHandEvent args)
    {
        if (!ent.Comp.TriggerOnInteract)
            return;

        if (!TryPlayLoudSpeaker(ent!))
            return;

        args.Handled = true;
    }
}
