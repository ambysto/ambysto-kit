namespace Ambysto.Kit.Safety;

/// <summary>The owner of one folder or file on an item's path.</summary>
/// <param name="Path">The canonical path of that folder or file.</param>
/// <param name="OwnerSid">The owner's security identifier.</param>
/// <param name="OwnedByCurrentUser">True only when the owner is the current user's own SID (a folder owned by Administrators is not).</param>
public readonly record struct OwnerFact(string Path, string OwnerSid, bool OwnedByCurrentUser);

/// <summary>The facts an app's allowed-root rule may use (docs/spec/safety.md, "Allowed roots").</summary>
public sealed class ItemFacts
{
    internal ItemFacts(string canonicalPath, string profileFolder, string profilesFolder, ulong volumeSerial, ulong systemVolumeSerial,
        IReadOnlyList<OwnerFact> owners, bool isSystemLocation)
    {
        CanonicalPath = canonicalPath; ProfileFolder = profileFolder; ProfilesFolder = profilesFolder; VolumeSerial = volumeSerial;
        SystemVolumeSerial = systemVolumeSerial; Owners = owners; IsSystemLocation = isSystemLocation;
    }

    /// <summary>The item's canonical path.</summary>
    public string CanonicalPath { get; }
    /// <summary>The current user's profile folder.</summary>
    public string ProfileFolder { get; }
    /// <summary>The folder that holds every user's profile.</summary>
    public string ProfilesFolder { get; }
    /// <summary>The serial of the item's volume.</summary>
    public ulong VolumeSerial { get; }
    /// <summary>The serial of the volume that holds the profile folder.</summary>
    public ulong SystemVolumeSerial { get; }
    /// <summary>Owners of the item and of every folder above it, the item first, up to the volume root.</summary>
    public IReadOnlyList<OwnerFact> Owners { get; }
    /// <summary>True when the item is inside a system location of the spec.</summary>
    public bool IsSystemLocation { get; }
}

/// <summary>A decision of an app's allowed-root rule.</summary>
public readonly record struct AllowedRootDecision
{
    private AllowedRootDecision(bool isAllowed, string? message) { IsAllowed = isAllowed; Message = message; }

    /// <summary>True when the app may change this location.</summary>
    public bool IsAllowed { get; }
    /// <summary>The app's reason text when refused.</summary>
    public string? Message { get; }

    /// <summary>Allows the location.</summary>
    /// <returns>An allowing decision.</returns>
    public static AllowedRootDecision Allow() => new(true, null);

    /// <summary>Refuses the location with the app's own text.</summary>
    /// <param name="message">What the user is told.</param>
    /// <returns>A refusing decision.</returns>
    public static AllowedRootDecision Refuse(string message) => new(false, message);
}

/// <summary>An app's rule for where it may move items (spec step 8 / Windows step 12). It runs only after protection passed.</summary>
public interface IAllowedRootRule
{
    /// <summary>Whether the app may move this item.</summary>
    /// <param name="item">Facts about the item.</param>
    /// <returns>The decision.</returns>
    AllowedRootDecision Evaluate(ItemFacts item);

    /// <summary>Whether the app may put an item back into this folder (a restore is judged differently from a delete).</summary>
    /// <param name="destinationParent">Facts about the destination's existing parent folder.</param>
    /// <returns>The decision.</returns>
    AllowedRootDecision EvaluateRestore(ItemFacts destinationParent);
}
