namespace FaunaLens.Services;

public static class ModelConfiguration
{
    // BioCLIP v1 image encoder exported to ONNX and derived from Imageomics BioCLIP.
    // The original Imageomics release is distributed as OpenCLIP/PyTorch weights;
    // this ONNX artifact is used because the Android app runs with ONNX Runtime.
#if SUPER
    // BioCLIP 2 (imageomics) exportado para ONNX pelo CI, anexado à release "super-versao".
    public const string BioClipOnnxUrl =
        "https://github.com/D19go/BioDex/releases/download/super-versao/bioclip2_image.onnx";
#else
    public const string BioClipOnnxUrl =
        "https://huggingface.co/m3r1al/naturalist-bioclip-v1/resolve/main/image_encoder.onnx?download=true";
#endif
}
