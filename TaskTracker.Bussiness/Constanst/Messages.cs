using System;
using System.Collections.Generic;
using System.Text;

namespace TaskTracker.Bussiness.Constanst
{
    public static class Messages
    {
        public static string MaintenanceTime = "Sistem bakımda";
        public static string AuthorizationDenied = "Yetkiniz Yok";
        public static string UserRegistered = "Kullanıcı başarıyla kaydedildi";
        public static string UserNotFound = "Kullanıcı bulunamadı";
        public static string PasswordError = "Şifre hatalı";
        public static string SuccessfulLogin = "Sisteme giriş başarılı";
        public static string UserAlreadyExists = "Bu kullanıcı zaten mevcut";
        public static string AccessTokenCreated = "Access token başarıyla oluşturuldu";
        public static string CodeNotFound = "Kod yanlış veya süresi geçmiş";
        public static string EmailIsCorrect = "Email doğrulandı giriş yapabilirsiniz";
        public static string UserPassive = "Kullanıcı aktif değil";
        public static string EmailNotVerified = "Email doğrulanmadı";
        public static string VerifyEmailCode = "Kayıt başarılı, mailine gönderilen kodu gir.";
        public static string CodeExpired = "Kod süresi doldu.";
        public static string RefreshTokenInvalid = "Refresh token geçersiz";
        public static string RefreshTokenExpired = "Refresh token süresi dolmuş";
        public static string DataGettingSuccess = "Veri başarıyla çekildi";
        public static string DataAdded = "Veri başarıyla eklendi";
        public static string DataUpdated = "Veri başarıyla güncellendi";
        public static string DataDeleted = "Veri başarıyla silindi";
        public static string DataNotFound = "Veri bulunamadı";
        public static string DataListed = "Veri başarıyla listelendi";
        public static string TaskShared = "Görev başarıyla paylaşıldı";
        public static string TaskAlreadyShared = "Görev zaten bu kullanıcıyla paylaşılmış";
        public static string UserCannotShareTaskWithSelf = "Görevi kendinle paylaşamazsın";
        public static string TaskShareInvitationAlreadySent = "Task share invitation has already been sent.";
        public static string TaskShareInvitationSent = "Task share invitation sent successfully.";
        public static string NotificationNotFound = "Notification not found.";
        public static string InvitationNotFound = "Invitation not found.";
        public static string TaskAccepted = "Task invitation accepted successfully.";
        public static string TaskRejected = "Task invitation rejected successfully.";
        public static string InvitationAlreadyResponded = "You have already responded to this invitation.";
        public static string InvitationExpired = "This invitation has expired.";
        public static string PasswordRecoveryInstructionsSent = "If an eligible account exists, password recovery instructions have been sent.";


    }
}
