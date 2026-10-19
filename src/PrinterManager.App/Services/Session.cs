using PrinterManager.App.Models;

namespace PrinterManager.App.Services;

/// <summary>Usuário autenticado e regras de permissão por perfil (RF11, RN03, RN07, RN12).</summary>
public static class Session
{
    public static User? CurrentUser { get; private set; }

    public static bool IsAuthenticated => CurrentUser is not null;
    public static bool IsAdmin => CurrentUser?.Role == UserRole.Administrador;

    /// <summary>Cadastrar/editar impressoras, registrar manutenções, reconhecer alertas.</summary>
    public static bool CanEdit => CurrentUser?.Role is UserRole.Administrador or UserRole.Tecnico;

    /// <summary>RN12: reset de contador de peça apenas Técnico ou Administrador.</summary>
    public static bool CanResetParts => CanEdit;

    public static string UserLabel => CurrentUser is null ? "Sistema" : CurrentUser.UserName;

    public static void SignIn(User user) => CurrentUser = user;
    public static void SignOut() => CurrentUser = null;

    public static void RequireEdit()
    {
        if (!CanEdit)
            throw new UnauthorizedAccessException("Seu perfil possui apenas permissão de leitura.");
    }

    public static void RequireAdmin()
    {
        if (!IsAdmin)
            throw new UnauthorizedAccessException("Esta operação é restrita ao perfil Administrador.");
    }
}
