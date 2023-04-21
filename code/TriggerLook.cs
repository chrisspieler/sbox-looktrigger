using Editor;
using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LookTrigger;

/// <summary>
/// Fires an <c>OnTrigger</c> Output when a compatible entity touching this trigger has
/// looked in the direction of <c>LookTarget</c> within a specified <c>FieldOfView</c> for a 
/// specified <c>LookTime</c> without a specified <c>Timeout</c> period having elapsed.
/// </summary>
[Library("trigger_look"), HammerEntity, Solid]
[Title("Look Trigger"), Category("Triggers"), Icon("preview")]
public partial class TriggerLook : LookTrigger.BaseTrigger
{
    /// <summary>
    /// If enabled, a large amount of debug information will be printed to console
    /// whenever a compatible entity is touching this trigger.
    /// </summary>
    [ConVar.Server("looktrigger_debug")]
    public static bool DebugInfo { get; set; } = false;

    /// <summary>
    /// The amount of time that has elapsed since a compatible entity has touched this trigger.
    /// </summary>
    [Net]
    protected TimeSince SinceActivated { get; set; }
    /// <summary>
    /// The amount of time that has elapsed since a compatible entity that is touching this trigger
    /// has begun to look at <c>LookTarget</c>. Will be set to zero if the the entity leaves this 
    /// trigger or if while in this trigger, their <c>AimRay</c> ever points away from the target
    /// to a degree that would, based on the value of <c>FieldOfView</c>, count as it not look at
    /// the target anymore.
    /// </summary>
    [Net]
    protected TimeSince SinceLookAt { get; set; }
    /// <summary>
    /// Will be true if all touching entities were looking at <c>LookTarget</c> in the previous
    /// tick, or false if not.
    /// </summary>
    protected bool WasLooking { get; set; } = false;
    /// <summary>
    /// Will be true if the amount of time that has elapsed since a compatible entity has entered
    /// this trigger without leaving has exceeded the period of time specified by <c>Timeout</c>.
    /// Will be set to false once all compatible entities have left the tringger, unless
    /// <c>FireOnce</c> is set to true.
    /// </summary>
    protected bool TimedOut { get; set; } = false;

    /// <summary>
    /// <para>After a compatible entity enters this trigger, this trigger will be deleted once
    /// any of the following conditions are met:</para>
    /// 
    /// * <c>OnTrigger</c> is fired.<br/>
    /// * <c>OnTimeout</c> is fired.<br/>
    /// * All compatible entities have left the trigger area.
    /// </summary>
    [Property]
    public bool FireOnce { get; set; } = false;

    /// <summary>
    /// The entity whose direction relative to an entity that touches this trigger shall be 
    /// tested.
    /// </summary>
    [Property]
    public EntityTarget LookTarget { get; set; }

    [Net]
    private Entity Target { get; set; }

    /// <summary>
    /// Determines the amount of time for which an entity touching this trigger must look at
    /// <c>LookTarget</c> before <c>OnTriggered</c> is invoked, and the <c>OnTrigger</c> Output
    /// is fired.
    /// </summary>
    [Property]
    public float LookTime { get; set; } = 0.5f;

    /// <summary>
    /// <para>Given the dot product of the forward direction of a touching entity's <c>AimRay</c> and 
    /// the direction between said touching entity and <c>LookTarget</c>, <c>FieldOfView</c> 
    /// is the smallest value of that dot product for which the touching entity will be considered 
    /// to have "seen" the target entity.</para>
    /// 
    /// <para>This value will range from -1 to 1, which in practice looks like this:</para>
    /// 
    /// * At 0.95, the touching entity is pretty much looking directly at the target.<br/>
    /// * At 0.5, the target might barely appear on screen from the perspective of the touching entity.<br/>
    /// * At 0, the direction of the target is perpendicular to the direction of the touching
    /// entity's gaze. <br/>
    /// * At -1, the direction of the touching entity's gaze is exactly the opposite of the
    /// direction between the touching entity and the target entity.
    /// 
    /// <para>A value of -1 for <c>FieldOfView</c> means that the <c>LookTarget</c> will be seen
    /// no matter where the touching entity is looking.</para>
    /// </summary>
    [Property]
    public float FieldOfView { get; set; } = 0.5f;

    /// <summary>
    /// Determines the amount of time for which a compatible entity may remain
    /// within this trigger without <c>OnTrigger</c> being fired before 
    /// <c>OnTimeout</c> is fired. If set to 0, then compatible entities have
    /// an indefinite amount of time to fire <c>OnTrigger</c>.
    /// </summary>
    [Property]
    public float Timeout { get; set; } = 4f;

    /// <summary>
    /// Fired when a compatible entity within this trigger has looked at <c>LookTarget</c>
    /// within the range of angles defined by <c>FieldOfView</c> for an unbroken period
    /// of time defined by <c>LookTime</c> without the <c>Timeout</c> interval having
    /// elapsed.
    /// </summary>
    protected Output OnTrigger { get; set; }
    /// <summary>
    /// Fired when a compatible entity within this trigger has failed to look
    /// at <c>LookTarget</c> directly enough or for long enough before the <c>Timeout</c>
    /// interval had elapsed.
    /// </summary>
    protected Output OnTimeout { get; set; }

    [Event.Tick.Server]
    private void OnTick()
    {
        if (!Enabled || TimedOut || Target == null || TouchingEntityCount == 0)
        {
            return;
        }

        if (DebugInfo)
        {
            PrintDebugInfo(TouchingEntities.First());
        }

        if (Timeout != 0 && SinceActivated > Timeout)
        {
            OnTimedOut(TouchingEntities.First());
            return;
        }

        if (AllTouchersAreLooking())
        {
            if (!WasLooking)
            {
                SinceLookAt = 0;
                WasLooking = true;
                return;
            }
            if (SinceLookAt >= LookTime)
            {
                OnTriggered(TouchingEntities.First());
            }
        }
        else
        {
            SinceLookAt = 0;
            WasLooking = false;
        }
    }

    /// <summary>
    /// Prints verbose debug information to determine the internal state of this trigger.
    /// </summary>
    /// <param name="toucher">The first of compatible entities that are touching this trigger.</param>
    private void PrintDebugInfo(Entity toucher)
    {
        Log.Info($"Look Trigger ({Name})");
        Log.Info($"Time Since Activated: {SinceActivated.Relative}");
        Log.Info($"Time Since Look At: {(WasLooking ? SinceLookAt.Relative : "NOT LOOKING")}");
        if (Target?.IsValid != true)
        {
            Log.Info($"Look Angle: TARGET NOT FOUND");
        }
        else
        {
            var directionToTarget = (Target.Position - toucher.Position).Normal;
            var angleOfView = toucher.AimRay.Forward.Dot(directionToTarget);
            Log.Info($"Look Angle: {angleOfView}");
        }

    }

    /// <summary>
    /// Returns true if all of the compatible entities within this trigger are looking
    /// at <c>LookTarget</c> within the specified <c>FieldOfview</c>.
    /// </summary>
    protected virtual bool AllTouchersAreLooking()
    {
        foreach (Entity toucher in TouchingEntities)
        {
            var directionToTarget = (Target.Position - toucher.Position).Normal;
            var angleOfView = toucher.AimRay.Forward.Dot(directionToTarget);
            if (angleOfView < FieldOfView)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Invoked whenever a non-zero <c>Timeout</c> period is specified
    /// and elasped while a compatible entity is touching this trigger
    /// without having fired <c>OnTrigger</c>.
    /// </summary>
    /// <param name="toucher">The first of the compatible entities that are touching this trigger.</param>
    public virtual void OnTimedOut(Entity toucher)
    {
        OnTimeout.Fire(toucher);

        if (FireOnce)
        {
            _ = DeleteAsync(Time.Delta);
        }
        else
        {
            TimedOut = true;
        }
    }

    /// <summary>
    /// Invoked whenever a compatible entity within this trigger has looked
    /// in the direction of <c>LookTarget</c> for the specified <c>LookTime</c>
    /// and within the specified <c>FieldOfView</c> without a non-zero <c>Timeout</c>
    /// period having elapsed in the meantime.
    /// </summary>
    /// <param name="toucher">The first of the compatible entities that are touching this trigger.</param>
    public virtual void OnTriggered(Entity toucher)
    {
        OnTrigger.Fire(toucher);

        if (FireOnce)
        {
            _ = DeleteAsync(Time.Delta);
        }
    }

    /// <summary>
    /// Invoked when a compatible entity begins to touch this trigger after no 
    /// compatible entities were touching this trigger.
    /// </summary>
    /// <param name="toucher">The entity that began to touch this trigger.</param>
    public override void OnTouchStartAll(Entity toucher)
    {
        base.OnTouchStartAll(toucher);

        Target = LookTarget.GetTarget();

        if (Target == null)
        {
            Log.Error($"({Name}) Unable to find target: {LookTarget.Name}");
        }

        SinceActivated = 0;
        WasLooking = false;
    }

    /// <summary>
    /// Invoked when the last of the compatible entities touching this trigger
    /// has stopped touching this trigger.
    /// </summary>
    /// <param name="toucher">The entity that had stopped touching this trigger.</param>
    public override void OnTouchEndAll(Entity toucher)
    {
        base.OnTouchEndAll(toucher);

        if (FireOnce)
        {
            _ = DeleteAsync(Time.Delta);
            return;
        }

        SinceActivated = 0;
        WasLooking = false;
        TimedOut = false;
    }
}
