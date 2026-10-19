using PrinterManager.App.Data;
using PrinterManager.App.Models;

namespace PrinterManager.App.Services;

public sealed record LoginResult(bool Success, string Message, User? User = null);

/// <summary>Autenticação, bloqueio por tentativas (RN11) e gestão de usuários (RF11, RN03).</summary>
public static class AuthService
{
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    private const int BcryptWorkFactor = 12;
    private const int MinPasswordLength = 8;

    public static bool HasAnyUser()
    {
        using var db = AppDbContext.Create();
        return db.Users.Any();
    }

    public static LoginResult Login(string userName, string password)
    {
        var result = CheckCredentials(userName, password, "Login");
        if (!result.Success || result.User is null) return result;

        using (var db = AppDbContext.Create())
        {
            var user = db.Users.Find(result.User.Id)!;
            user.LastLoginAt = DateTime.UtcNow;
            db.SaveChanges();
            Session.SignIn(user);
        }

        AuditService.Write("Login", "Usuario", result.User.Id.ToString(), "Login efetuado com sucesso");
        return result;
    }

    /// <summary>RN09: confirmação por senha de um administrador para operações destrutivas.</summary>
    public static LoginResult VerifyAdministrator(string userName, string password)
    {
        var result = CheckCredentials(userName, password, "Confirmacao de administrador");
        if (result.Success && result.User?.Role != UserRole.Administrador)
            return new LoginResult(false, "O usuário informado não é Administrador.");
        return result;
    }

    private static LoginResult CheckCredentials(string userName, string password, string context)
    {
        userName = userName.Trim();
        using var db = AppDbContext.Create();
        var user = db.Users.FirstOrDefault(u => u.UserName.ToLower() == userName.ToLower());

        if (user is null)
        {
            AuditService.Write($"{context} negado", "Usuario", null, $"Usuario inexistente: {userName}");
            return new LoginResult(false, "Usuário ou senha inválidos.");
        }

        if (!user.IsActive)
            return new LoginResult(false, "Usuário desativado. Procure um administrador.");

        var now = DateTime.UtcNow;
        if (user.LockoutUntil is { } until && until > now)
        {
            var minutes = Math.Ceiling((until - now).TotalMinutes);
            return new LoginResult(false, $"Usuário bloqueado por excesso de tentativas. Tente novamente em {minutes:0} minuto(s).");
        }

        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
        {
            user.FailedLoginAttempts++;
            string message;
            if (user.FailedLoginAttempts >= MaxFailedAttempts)
            {
                user.LockoutUntil = now.Add(LockoutDuration);
                user.FailedLoginAttempts = 0;
                message = $"Usuário bloqueado por {LockoutDuration.TotalMinutes:0} minutos após {MaxFailedAttempts} tentativas incorretas.";
                db.SaveChanges();
                AuditService.Write("Usuario bloqueado", "Usuario", user.Id.ToString(), message, actor: user);
            }
            else
            {
                var remaining = MaxFailedAttempts - user.FailedLoginAttempts;
                message = $"Usuário ou senha inválidos. Restam {remaining} tentativa(s) antes do bloqueio.";
                db.SaveChanges();
                AuditService.Write($"{context} negado", "Usuario", user.Id.ToString(), "Senha incorreta", actor: user);
            }
            return new LoginResult(false, message);
        }

        user.FailedLoginAttempts = 0;
        user.LockoutUntil = null;
        db.SaveChanges();
        return new LoginResult(true, "OK", user);
    }

    public static List<User> GetUsers()
    {
        Session.RequireAdmin();
        using var db = AppDbContext.Create();
        return db.Users.OrderBy(u => u.UserName).ToList();
    }

    /// <summary>Cria usuário. O primeiro usuário do sistema é criado sem sessão e sempre como Administrador.</summary>
    public static User CreateUser(string userName, string fullName, string? email, UserRole role, string password)
    {
        var firstUser = !HasAnyUser();
        if (!firstUser) Session.RequireAdmin();
        if (firstUser) role = UserRole.Administrador;

        userName = userName.Trim();
        if (userName.Length < 3) throw new InvalidOperationException("O login deve ter ao menos 3 caracteres.");
        ValidatePassword(password);

        using var db = AppDbContext.Create();
        if (db.Users.Any(u => u.UserName.ToLower() == userName.ToLower()))
            throw new InvalidOperationException("Já existe um usuário com este login.");

        var user = new User
        {
            UserName = userName,
            FullName = fullName.Trim(),
            Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim(),
            Role = role,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, BcryptWorkFactor)
        };
        db.Users.Add(user);
        db.SaveChanges();

        AuditService.Write("Usuario criado", "Usuario", user.Id.ToString(),
            $"Login={user.UserName}; Perfil={user.Role}", actor: firstUser ? user : null);
        return user;
    }

    public static void UpdateUser(int id, string fullName, string? email, UserRole role, bool isActive)
    {
        Session.RequireAdmin();
        using var db = AppDbContext.Create();
        var user = db.Users.Find(id) ?? throw new InvalidOperationException("Usuário não encontrado.");

        if (user.Id == Session.CurrentUser!.Id && (role != UserRole.Administrador || !isActive))
            throw new InvalidOperationException("Você não pode remover o próprio perfil de Administrador nem se desativar.");

        if (user.Role == UserRole.Administrador && (role != UserRole.Administrador || !isActive) &&
            db.Users.Count(u => u.Role == UserRole.Administrador && u.IsActive) <= 1)
            throw new InvalidOperationException("O sistema precisa de ao menos um Administrador ativo.");

        var details = $"Perfil {user.Role}->{role}; Ativo {user.IsActive}->{isActive}; Nome '{user.FullName}'->'{fullName}'";
        user.FullName = fullName.Trim();
        user.Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        user.Role = role;
        user.IsActive = isActive;
        db.SaveChanges();

        AuditService.Write("Usuario alterado", "Usuario", id.ToString(), details);
    }

    public static void ResetPassword(int id, string newPassword)
    {
        Session.RequireAdmin();
        ValidatePassword(newPassword);
        using var db = AppDbContext.Create();
        var user = db.Users.Find(id) ?? throw new InvalidOperationException("Usuário não encontrado.");
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword, BcryptWorkFactor);
        user.FailedLoginAttempts = 0;
        user.LockoutUntil = null;
        db.SaveChanges();
        AuditService.Write("Senha redefinida", "Usuario", id.ToString(), $"Senha de {user.UserName} redefinida pelo administrador");
    }

    public static void Unlock(int id)
    {
        Session.RequireAdmin();
        using var db = AppDbContext.Create();
        var user = db.Users.Find(id) ?? throw new InvalidOperationException("Usuário não encontrado.");
        user.FailedLoginAttempts = 0;
        user.LockoutUntil = null;
        db.SaveChanges();
        AuditService.Write("Usuario desbloqueado", "Usuario", id.ToString(), user.UserName);
    }

    public static void ChangeOwnPassword(string currentPassword, string newPassword)
    {
        var me = Session.CurrentUser ?? throw new UnauthorizedAccessException();
        ValidatePassword(newPassword);
        using var db = AppDbContext.Create();
        var user = db.Users.Find(me.Id)!;
        if (!BCrypt.Net.BCrypt.Verify(currentPassword, user.PasswordHash))
            throw new InvalidOperationException("A senha atual está incorreta.");
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword, BcryptWorkFactor);
        db.SaveChanges();
        AuditService.Write("Senha alterada", "Usuario", me.Id.ToString(), "Usuario alterou a propria senha");
    }

    public static void Logout()
    {
        if (Session.CurrentUser is { } user)
            AuditService.Write("Logout", "Usuario", user.Id.ToString(), "Sessao encerrada");
        Session.SignOut();
    }

    private static void ValidatePassword(string password)
    {
        if (password.Length < MinPasswordLength || !password.Any(char.IsLetter) || !password.Any(char.IsDigit))
            throw new InvalidOperationException($"A senha deve ter ao menos {MinPasswordLength} caracteres, com letras e números.");
    }
}
