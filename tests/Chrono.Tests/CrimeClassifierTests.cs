using Chrono.Domain;
using Xunit;

namespace Chrono.Tests;

/// <summary>S20 — pure act→crime classification (ADR-04 D1). No fakes needed.</summary>
public class CrimeClassifierTests
{
    private static ActSample Sample(
        DeathCauseKind weaponOut = DeathCauseKind.None,
        bool aiming = false,
        bool inVehicle = false,
        float speed = 0f,
        DeathCauseKind kill = DeathCauseKind.None,
        bool pedDamage = false,
        bool vehicleDamage = false,
        float crosshair = float.MaxValue,
        int witnesses = 1)
        => new(weaponOut, aiming, inVehicle, speed, kill, pedDamage, vehicleDamage, crosshair, witnesses);

    [Fact]
    public void GunKill_IsSevereMurder_FiveStars()
    {
        var c = CrimeClassifier.Classify(Sample(weaponOut: DeathCauseKind.Gun, kill: DeathCauseKind.Gun));
        Assert.NotNull(c);
        Assert.Equal(CrimeKind.Murder, c!.Kind);
        Assert.Equal(CrimeSeverity.Severe, c.Severity);
        Assert.Equal(5, c.Stars);
        Assert.Equal("murder", c.Name);
    }

    [Fact]
    public void MeleeKill_IsSevereMurder_FourStars()
    {
        var c = CrimeClassifier.Classify(Sample(weaponOut: DeathCauseKind.Melee, kill: DeathCauseKind.Melee));
        Assert.Equal(CrimeKind.Murder, c!.Kind);
        Assert.Equal(4, c.Stars);
    }

    [Fact]
    public void ExplosiveKill_IsSevereMurder_FiveStars()
    {
        var c = CrimeClassifier.Classify(Sample(weaponOut: DeathCauseKind.Explosive, kill: DeathCauseKind.Explosive));
        Assert.Equal(CrimeKind.Murder, c!.Kind);
        Assert.Equal(5, c.Stars);
    }

    [Fact]
    public void VehicleKill_DrivingFast_IsVehicularManslaughter_Moderate_ThreeStars()
    {
        var c = CrimeClassifier.Classify(Sample(
            weaponOut: DeathCauseKind.None, inVehicle: true, speed: 25f, kill: DeathCauseKind.Vehicle));
        Assert.Equal(CrimeKind.VehicularManslaughter, c!.Kind);
        Assert.Equal(CrimeSeverity.Moderate, c.Severity);
        Assert.Equal(3, c.Stars);
    }

    [Fact]
    public void VehicleKill_TooSlow_IsMurderNotManslaughter()
    {
        // Rolling over someone at walking speed = still a kill, but not "manslaughter at speed"
        var slow = CrimeClassifier.Classify(Sample(inVehicle: true, speed: 2f, kill: DeathCauseKind.Vehicle));
        Assert.Equal(CrimeKind.Murder, slow!.Kind);
        Assert.Equal(5, slow.Stars);   // source was a vehicle but slow → generic severe
    }

    [Fact]
    public void AimingGunAtPedCloseRange_IsAttemptedRobbery()
    {
        var c = CrimeClassifier.Classify(Sample(
            weaponOut: DeathCauseKind.Gun, aiming: true, crosshair: 3f));
        Assert.Equal(CrimeKind.AttemptedRobbery, c!.Kind);
        Assert.Equal(CrimeSeverity.Moderate, c.Severity);
        Assert.Equal(3, c.Stars);
    }

    [Fact]
    public void AimingGunAtPedBeyondRange_IsBrandishingNotRobbery()
    {
        var c = CrimeClassifier.Classify(Sample(
            weaponOut: DeathCauseKind.Gun, aiming: true, crosshair: 20f, witnesses: 2));
        Assert.Equal(CrimeKind.Brandishing, c!.Kind);
        Assert.Equal(CrimeSeverity.Minor, c.Severity);
        Assert.Equal(1, c.Stars);
    }

    [Fact]
    public void Brandishing_RequiresWitnesses()
    {
        var noWitnesses = CrimeClassifier.Classify(Sample(weaponOut: DeathCauseKind.Gun, aiming: true, witnesses: 0));
        Assert.Null(noWitnesses);   // nobody saw → classifier itself declines
    }

    [Fact]
    public void NonLethalPedDamage_IsAssault_TwoStars()
    {
        var c = CrimeClassifier.Classify(Sample(weaponOut: DeathCauseKind.Melee, pedDamage: true));
        Assert.Equal(CrimeKind.Assault, c!.Kind);
        Assert.Equal(CrimeSeverity.Minor, c.Severity);
        Assert.Equal(2, c.Stars);
    }

    [Fact]
    public void VehicleDamage_IsPropertyDamage_OneStar()
    {
        var c = CrimeClassifier.Classify(Sample(vehicleDamage: true));
        Assert.Equal(CrimeKind.PropertyDamage, c!.Kind);
        Assert.Equal(CrimeSeverity.Minor, c.Severity);
        Assert.Equal(1, c.Stars);
    }

    [Fact]
    public void NoAct_ReturnsNull()
    {
        Assert.Null(CrimeClassifier.Classify(Sample()));
        Assert.Null(CrimeClassifier.Classify(Sample(aiming: true, weaponOut: DeathCauseKind.None)));  // fists aimed = no act
    }

    [Fact]
    public void KillWinsOverRobberyAndAssault()
    {
        // A kill in the same window as a close-range aim = the gravest act wins
        var c = CrimeClassifier.Classify(Sample(
            weaponOut: DeathCauseKind.Gun, aiming: true, crosshair: 2f, kill: DeathCauseKind.Gun, pedDamage: true));
        Assert.Equal(CrimeKind.Murder, c!.Kind);
        Assert.Equal(5, c.Stars);
    }

    [Fact]
    public void BrandishWinsOverPropertyDamage()
    {
        var c = CrimeClassifier.Classify(Sample(
            weaponOut: DeathCauseKind.Gun, aiming: true, vehicleDamage: true, witnesses: 1));
        Assert.Equal(CrimeKind.Brandishing, c!.Kind);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(4f)]
    public void CrosshairAtOrUnderRobberyRange_IsRobbery(float dist)
    {
        var c = CrimeClassifier.Classify(Sample(weaponOut: DeathCauseKind.Gun, aiming: true, crosshair: dist));
        Assert.Equal(CrimeKind.AttemptedRobbery, c!.Kind);
    }

    [Fact]
    public void MeleeKillWhileDriving_IsMurderNotManslaughter()
    {
        // A knife kill from inside a slow vehicle is still a melee murder
        var c = CrimeClassifier.Classify(Sample(
            weaponOut: DeathCauseKind.Melee, inVehicle: true, speed: 5f, kill: DeathCauseKind.Melee));
        Assert.Equal(CrimeKind.Murder, c!.Kind);
        Assert.Equal(4, c.Stars);
    }
}
