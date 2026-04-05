using UnityEngine;
using Unity.Netcode.Components;

/// <summary>
/// Drop-in replacement for NetworkTransform that makes the OWNER authoritative
/// instead of the server. Required for client-controlled player movement:
/// without this, the server constantly overwrites the client's position,
/// making player 2+ appear frozen / unable to move.
///
/// Setup:
///   1. Remove the NetworkTransform component from your Player prefab.
///   2. Add this component in its place.
///   No other changes needed.
/// </summary>
[DisallowMultipleComponent]
public class OwnerNetworkTransform : NetworkTransform
{
    protected override bool OnIsServerAuthoritative() => false;
}
