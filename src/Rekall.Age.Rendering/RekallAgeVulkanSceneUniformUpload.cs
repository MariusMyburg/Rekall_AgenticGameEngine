using System.Numerics;
using System.Runtime.InteropServices;

namespace Rekall.Age.Rendering;

public static class RekallAgeVulkanSceneUniformUploadBuilder
{
    public static RekallAgeVulkanSceneGpuFrameUniform BuildFrameUniform(RekallAgeVulkanSceneFrameUniform frame)
    {
        return new RekallAgeVulkanSceneGpuFrameUniform(
            ToGpuMatrix(frame.ViewProjection),
            frame.LightDirection.X,
            frame.LightDirection.Y,
            frame.LightDirection.Z,
            0,
            frame.LightColor.X,
            frame.LightColor.Y,
            frame.LightColor.Z,
            frame.LightColor.W,
            frame.LightPosition.X,
            frame.LightPosition.Y,
            frame.LightPosition.Z,
            frame.LightPosition.W);
    }

    public static RekallAgeVulkanSceneGpuDrawPushConstants BuildDrawPushConstants(
        Matrix4x4 model,
        Vector4 materialFactors,
        Vector4 emissiveFactors)
    {
        return new RekallAgeVulkanSceneGpuDrawPushConstants(
            ToGpuMatrix(model),
            materialFactors.X,
            materialFactors.Y,
            materialFactors.Z,
            materialFactors.W,
            emissiveFactors.X,
            emissiveFactors.Y,
            emissiveFactors.Z,
            emissiveFactors.W);
    }

    public static RekallAgeVulkanSceneGpuMatrix4x4 ToGpuMatrix(Matrix4x4 matrix)
    {
        return new RekallAgeVulkanSceneGpuMatrix4x4(
            matrix.M11,
            matrix.M12,
            matrix.M13,
            matrix.M14,
            matrix.M21,
            matrix.M22,
            matrix.M23,
            matrix.M24,
            matrix.M31,
            matrix.M32,
            matrix.M33,
            matrix.M34,
            matrix.M41,
            matrix.M42,
            matrix.M43,
            matrix.M44);
    }
}

[StructLayout(LayoutKind.Sequential)]
public readonly record struct RekallAgeVulkanSceneGpuMatrix4x4(
    float M11,
    float M12,
    float M13,
    float M14,
    float M21,
    float M22,
    float M23,
    float M24,
    float M31,
    float M32,
    float M33,
    float M34,
    float M41,
    float M42,
    float M43,
    float M44);

[StructLayout(LayoutKind.Sequential)]
public readonly record struct RekallAgeVulkanSceneGpuFrameUniform(
    RekallAgeVulkanSceneGpuMatrix4x4 ViewProjection,
    float LightX,
    float LightY,
    float LightZ,
    float LightPad,
    float LightR,
    float LightG,
    float LightB,
    float LightA,
    float LightPositionX,
    float LightPositionY,
    float LightPositionZ,
    float LightPositionW);

[StructLayout(LayoutKind.Sequential)]
public readonly record struct RekallAgeVulkanSceneGpuDrawPushConstants(
    RekallAgeVulkanSceneGpuMatrix4x4 Model,
    float MetallicFactor,
    float RoughnessFactor,
    float NormalScale,
    float OcclusionStrength,
    float EmissiveR,
    float EmissiveG,
    float EmissiveB,
    float EmissiveStrength);
