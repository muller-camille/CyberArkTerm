namespace CyberArkTerm.Core;

/// <summary>
/// Méthodes d'authentification proposées par le PVWA (endpoint <c>API/auth/{méthode}/Logon</c>).
/// </summary>
public enum AuthMethod
{
    CyberArk,
    LDAP,
    RADIUS,
    Windows,
}
