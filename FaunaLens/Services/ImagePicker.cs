using Android.Content;
using Android.Provider;

namespace FaunaLens.Services;

// Abre a galeria para escolher fotos. Não usa o gerenciador de arquivos (ACTION_OPEN_DOCUMENT):
// no Android 13 ou superior abre o seletor de fotos do sistema; nas versões anteriores, o seletor de conteúdo de imagens.
public static class ImagePicker
{
    public static Intent Create(bool multiple, int max)
    {
        if (Android.OS.Build.VERSION.SdkInt >= (Android.OS.BuildVersionCodes)33)
        {
            var photoPicker = new Intent(MediaStore.ActionPickImages);
            if (multiple)
                photoPicker.PutExtra(MediaStore.ExtraPickImagesMax, max);
            return photoPicker;
        }

        var picker = new Intent(Intent.ActionGetContent);
        picker.SetType("image/*");
        if (multiple)
            picker.PutExtra(Intent.ExtraAllowMultiple, true);
        return picker;
    }
}
