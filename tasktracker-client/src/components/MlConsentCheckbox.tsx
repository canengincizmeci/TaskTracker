import { useId } from "react";

export default function MlConsentCheckbox({ checked, disabled, onChange }: {
  checked: boolean;
  disabled: boolean;
  onChange: (checked: boolean) => void;
}) {
  const description = useId();

  return <div className="task-ml-consent" lang="tr">
    <label>
      <input type="checkbox" name="mlConsentGranted" checked={checked} disabled={disabled}
        aria-describedby={description} onChange={event => onChange(event.currentTarget.checked)} />
      <span>Bu görevdeki verilerimin, kimlik bilgilerim ayıklanarak makine öğrenmesi modellerinin geliştirilmesi ve değerlendirilmesi amacıyla kullanılmasına izin veriyorum.</span>
    </label>
    <p id={description}>Bu izin isteğe bağlıdır. Verileriniz için geçerlidir ve görev ayarlarından geri çekilebilir. Geri çekme, sonraki dışa aktarımlarda geçerli olur.</p>
  </div>;
}
