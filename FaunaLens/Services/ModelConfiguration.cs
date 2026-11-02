namespace FaunaLens.Services;

public static class ModelConfiguration
{
    // BioCLIP v1 image encoder exported to ONNX and derived from Imageomics BioCLIP.
    // The original Imageomics release is distributed as OpenCLIP/PyTorch weights;
    // this ONNX artifact is used because the Android app runs with ONNX Runtime.
    public const string BioClipOnnxUrl =
        "https://huggingface.co/m3r1al/naturalist-bioclip-v1/resolve/main/image_encoder.onnx?download=true";
}
