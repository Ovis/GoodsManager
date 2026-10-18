using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;

namespace GoodsManager.Web.Backups;

/// <summary>Data Protection標準のファイル保存を維持し、鍵のスナップショットと更新を直列化</summary>
public sealed class BackupKeyRepository(FileSystemXmlRepository inner, BackupGate gate) : IXmlRepository
{
    public IReadOnlyCollection<XElement> GetAllElements() { lock (gate.KeyLock) return inner.GetAllElements(); }
    public void StoreElement(XElement element, string friendlyName) { lock (gate.KeyLock) inner.StoreElement(element, friendlyName); }
}
