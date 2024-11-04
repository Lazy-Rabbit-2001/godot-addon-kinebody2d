using Godot;
using System;
using System.Reflection;
using System.Threading.Tasks;

namespace GodotKineBody;

/// <summary>
/// C# edition of <c>KineBody2D</c>.<br/>
/// <b>Note:</b> During the high consumption of the <c>CharacterBody2D.MoveAndSlide()</c>, it is not couraged to run with the overnumbered use of <c>KineBody2DCs</c>.
/// </summary>

[Tool]
[GlobalClass, Icon("res://addons/kinebody/kinebody2d/KineBody2DCs.svg")]
public partial class KineBody2DCs : CharacterBody2D
{
    /// <summary>
    /// Definitions about the transformation method on <c>MotionVector</c>.<br/>
    /// <c>MotionVectorDirectionUpDirection</c>: The direction of the <c>MotionVector</c> equals to <c>UpDirection.Rotated(Math.PI / 2.0d)</c>.<br/>
    /// <c>MotionVectorDirectionGlobalRotation</c>: The direction of the <c>MotionVector</c> is rotated by <c>GlobalRotation</c>.<br/>
    /// <c>MotionVectorDirectionDefault</c>: The <c>MotionVector</c> is an alternative identifier of <c>CharacterBody2D.Velocity</c>.
    /// </summary>
    /// <seealso cref="MotionVector"/>
    public enum MotionVectorDirectionEnum
    {
        UpDirection,
        GlobalRotation,
        Default,
    }

    /// <summary>
    /// Emitted when the body collides with the side of the other body.
    /// </summary>
    [Signal]
    public delegate void CollidedWallEventHandler();
    /// <summary>
    /// Emitted when the body collides with the bottom of the other body.
    /// </summary>
    [Signal]
    public delegate void CollidedCeilingEventHandler();
    /// <summary>
    /// Emitted when the body collides with the top of the other body
    /// </summary>
    [Signal]
    public delegate void CollidedFloorEventHandler();

    /// <summary>
    /// The mass of the body, which will affect the impulse that will be applied to the body.
    /// <b>Note</b> Due to the limitation of assignment for auto-implemented properties in C#, the default value is also the minimum that it can be modified to in the inspector.
    /// </summary>
    [Export(PropertyHint.Range, "0.1, 99999.0, 0.1, or_greater, hide_slider, suffix:kg")]
    public double Mass
    { 
        get => (double)PhysicsServer2D.BodyGetParam(GetRid(), PhysicsServer2D.BodyParameter.Mass); 
        set => PhysicsServer2D.BodySetParam(GetRid(), PhysicsServer2D.BodyParameter.Mass, Mathf.Max(0.001d, value));
    }
    /// <summary>
    /// The option that defines which transformation method will be applied to <c>motion_vector</c>.
    /// </summary>
    [Export]
    public MotionVectorDirectionEnum MotionVectorDirection { get; set; } = MotionVectorDirectionEnum.UpDirection;
    /// <summary>
    /// The <c>CharacterBody2D.velocity</c> of the body, transformed by a specific method defined by <c>motion_vector_direction</c>.
    /// </summary>
    [Export(PropertyHint.None, "suffix:px/s")]
    public Vector2 MotionVector
    {
        get => GetMotionVector();
        set => SetMotionVector(value);
    }
    /// <summary>
    /// The scale of the gravity acceleration. The actual gravity acceleration is calculated as <c>gravity_scale * get_gravity</c>.
    /// </summary>
    [Export(PropertyHint.Range, "0.0, 999.0, 0.1, or_greater, hide_slider, suffix:x")]
    public double GravityScale { get; set; } = 1.0d;
    /// <summary>
    /// The maximum of falling speed. If set to <c>0</c>, there will be no limit on maximum falling speed and the body will keep falling faster and faster.
    /// </summary>
    [Export(PropertyHint.Range, "0.0, 12500.0, 0.1, or_greater, hide_slider, suffix:px/s")]
    public double MaxFallingSpeed { get; set; } = 1500.0d;
    /// <summary>
    /// Duration of the rotation synchronization. See <c>SynchronizeGlobalRotationToUpDirection()</c>
    /// </summary>
    [ExportGroup("Rotation", "Rotation")]
    [Export(PropertyHint.Range, "0.0, 999.0, 0.1, or_greater, hide_slider, suffix:s")]
    public double RotationSynchronizingDuration { get; set; } = 0.1d;

    // Tween for the rotation synchronization.
    private Tween _rotationSynchronizingTween;


    private double GetDelta()
    {
        return Engine.IsInPhysicsFrame() ? GetPhysicsProcessDeltaTime() : GetProcessDeltaTime();
    }

#region == Main physics methods ==
    /// <summary>
    /// Moves the kine body instance.<br/><br/>
    /// The <c>speedScale</c> will affect the final motion, while the <c>globalRotationSyncUpDirection</c> will synchronize <c>Node2D.GlobalRotation</c> to <c>CharacterBody2D.UpDirection</c> by calling <c>SynchronizeGlobalRotationToUpDirection()</c>.
    /// </summary>
    /// <param name="speedScale"></param>
    /// <param name="globalRotationSyncUpDirection"></param>
    /// <returns>returns [code]true[/code] when it collides with other physics bodies.</returns>
    public bool MoveKineBody(float speedScale = 1.0f, bool globalRotationSyncUpDirection = true)
    {
        var g = GetGravity();
        var gDir = GetGravity().Normalized();

        // `up_direction` will not work in floating mode
        if (MotionMode == CharacterBody2D.MotionModeEnum.Grounded) {
            if (!Mathf.IsNaN(gDir.X) && !Mathf.IsNaN(gDir.Y) && !gDir.IsZeroApprox()) {
                UpDirection = -gDir;
            }
        }

        // Applying gravity
        if (GravityScale > 0.0d) {
            Velocity += g * (float)(GravityScale * GetDelta());
            var fV = Velocity.Project(gDir);
            if (MaxFallingSpeed > 0.0d && !Mathf.IsNaN(fV.X) && !Mathf.IsNaN(fV.Y) && fV.Dot(gDir) > 0.0d && fV.LengthSquared() > Mathf.Pow(MaxFallingSpeed, 2.0d)) {
                Velocity -= fV - fV.Normalized() * (float)MaxFallingSpeed;
            }
        }

        // Synchronizing global rotation to up direction
        if (globalRotationSyncUpDirection) {
            _ = SynchronizeGlobalRotationToUpDirection();
        }
        
        // Applying speed scale
        var tmpV = Velocity;
        Velocity *= speedScale;
        var ret = MoveAndSlide();
        Velocity = tmpV;

        // Handling signal emissions
        if (ret) {
            if (IsOnWall()) {
                EmitSignal(SignalName.CollidedWall);
            }
            if (IsOnCeiling()) {
                EmitSignal(SignalName.CollidedCeiling);
            }
            if (IsOnFloor()) {
                EmitSignal(SignalName.CollidedFloor);
            }
        }

        return ret;
    }

    /// <summary>
    /// Synchronizes <c>Node2D.GlobalRotation</c> to <c>CharacterBody2D.UpDirection</c>,
    /// that is to say, the global rotation of the body will be synchronized to <c>UpDirection.Angle() + Math.PI / 2.0d</c>.<br/><br/>
    /// <b>Note:</b> This is achieved by creating an object <c>Tween</c>, which may take more space of memory. Make sure to call this method within certain instances.
    /// </summary>
    public async Task SynchronizeGlobalRotationToUpDirection()
    {
        if (MotionMode != MotionModeEnum.Grounded) {
            return; // Non-ground mode does not support `up_direction`.
        }
        var targetRotation = UpDirection.Angle() + Math.PI / 2.0d;
        if (Mathf.IsEqualApprox(GlobalRotation, targetRotation)) {
            GlobalRotation = (float)targetRotation;
        } else if (_rotationSynchronizingTween == null) {
            // Creating a new tween for the rotation synchronization.
            _rotationSynchronizingTween = CreateTween().SetTrans(Tween.TransitionType.Sine);
            _rotationSynchronizingTween.TweenProperty(this, (NodePath)"global_rotation", targetRotation, RotationSynchronizingDuration);
            // Waiting for the tween to finish.
            await ToSignal(_rotationSynchronizingTween, Tween.SignalName.Finished);
            // Clearing the tween reference to avoid memory leak.
            _rotationSynchronizingTween.Kill();
            _rotationSynchronizingTween = null;
        }
    }
#endregion

#region == Setters and getters ==
    private void SetMotionVector(Vector2 value)
    {
        switch (MotionVectorDirection) {
            case MotionVectorDirectionEnum.Default:
                Velocity = value;
                break;
            case MotionVectorDirectionEnum.UpDirection:
                Velocity = value.Rotated((float)(UpDirection.Angle() + Math.PI / 2.0d));
                break;
            case MotionVectorDirectionEnum.GlobalRotation:
                Velocity = value.Rotated(GlobalRotation);
                break;
            default:
                break;
        }
    }
    private Vector2 GetMotionVector()
    {
        switch (MotionVectorDirection) {
            case MotionVectorDirectionEnum.UpDirection:
                return Velocity.Rotated((float)(-UpDirection.Angle() - Math.PI / 2.0d));
            case MotionVectorDirectionEnum.GlobalRotation:
                return Velocity.Rotated(-GlobalRotation);
            default:
                break;
        }
        return Velocity;
    }
#endregion

#region == Property settings ==
    public override bool _PropertyCanRevert(StringName property)
    {
        if (property == (StringName)"Mass") {
            return true;
        }
        return base._PropertyCanRevert(property);
    }

    public override Variant _PropertyGetRevert(StringName property)
    {
        if (property == (StringName)"Mass") {
            return 1.0d;
        }
        return base._PropertyGetRevert(property);
    }
#endregion
}

