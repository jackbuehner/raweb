using System.Collections;
using System.Collections.Generic;

namespace RAWeb.Sddl;

/// <summary>
/// A portable, ordered list of <see cref="CommonAce"/> entries representing an
/// access control list (DACL or SACL).
/// </summary>
public sealed class RawAcl : IReadOnlyList<CommonAce> {
  private readonly List<CommonAce> _aces = [];

  public byte Revision { get; }

  public RawAcl(byte revision = 2) {
    Revision = revision;
  }

  public int Count => _aces.Count;

  public CommonAce this[int index] => _aces[index];

  public void InsertAce(int index, CommonAce ace) {
    _aces.Insert(index, ace);
  }

  public IEnumerator<CommonAce> GetEnumerator() => _aces.GetEnumerator();

  IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
