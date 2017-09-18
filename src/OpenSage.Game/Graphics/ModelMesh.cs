﻿using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using LLGfx;
using OpenSage.Content;
using OpenSage.Data.W3d;
using OpenSage.Graphics.Effects;
using OpenSage.Graphics.Util;
using OpenSage.Mathematics;

namespace OpenSage.Graphics
{
    public sealed class ModelMesh : GraphicsObject
    {
        private readonly uint _numVertices;
        private readonly Buffer _vertexBuffer;

        private readonly uint _numIndices;
        private readonly Buffer _indexBuffer;

        private readonly DescriptorSet _pixelMeshDescriptorSet;

        private readonly DynamicBuffer _meshTransformConstantBuffer;
        private MeshTransformConstants _meshTransformConstants;

        private readonly DynamicBuffer _perDrawConstantBuffer;
        private PerDrawConstants _perDrawConstants;

        private List<DrawList> _drawListsOpaque;
        private List<DrawList> _drawListsTransparent;

        public string Name { get; }

        public ModelBone ParentBone { get; }

        public BoundingSphere BoundingSphere { get; }

        public ModelMeshMaterialPass[] MaterialPasses { get; }

        public bool Skinned { get; }

        internal ModelMesh(
            ContentManager contentManager,
            ResourceUploadBatch uploadBatch,
            W3dMesh w3dMesh,
            ModelBone parentBone)
        {
            Name = w3dMesh.Header.MeshName;
            ParentBone = parentBone;

            BoundingSphere = new BoundingSphere(
                w3dMesh.Header.SphCenter.ToVector3(),
                w3dMesh.Header.SphRadius);

            Skinned = w3dMesh.Header.Attributes.HasFlag(W3dMeshFlags.GeometryTypeSkin);

            _numVertices = (uint) w3dMesh.Vertices.Length;

            _vertexBuffer = CreateVertexBuffer(
                contentManager,
                uploadBatch,
                w3dMesh,
                Skinned);

            _numIndices = (uint) w3dMesh.Triangles.Length * 3;

            _indexBuffer = CreateIndexBuffer(
                contentManager,
                uploadBatch,
                w3dMesh);

            var materialsBuffer = CreateMaterialsBuffer(
                contentManager,
                uploadBatch,
                w3dMesh);

            var textures = CreateTextures(
                contentManager,
                uploadBatch,
                w3dMesh);

            _pixelMeshDescriptorSet = AddDisposable(contentManager.ModelEffect.CreateMeshPixelDescriptorSet());

            _pixelMeshDescriptorSet.SetStructuredBuffer(0, materialsBuffer);

            _pixelMeshDescriptorSet.SetTextures(1, textures);

            var remainingTextures = ModelEffect.MaxTextures - textures.Length;
            _pixelMeshDescriptorSet.SetTextures(1 + textures.Length, new Texture[remainingTextures]);

            _meshTransformConstantBuffer = AddDisposable(DynamicBuffer.Create<MeshTransformConstants>(contentManager.GraphicsDevice));

            _perDrawConstantBuffer = AddDisposable(DynamicBuffer.Create<PerDrawConstants>(contentManager.GraphicsDevice));

            MaterialPasses = new ModelMeshMaterialPass[w3dMesh.MaterialPasses.Length];
            for (var i = 0; i < w3dMesh.MaterialPasses.Length; i++)
            {
                MaterialPasses[i] = AddDisposable(new ModelMeshMaterialPass(
                    contentManager,
                    uploadBatch,
                    w3dMesh,
                    w3dMesh.MaterialPasses[i]));
            }

            var uniquePipelineStates = MaterialPasses
                .SelectMany(x => x.MeshParts.Select(y => y.PipelineState))
                .Distinct()
                .ToList();

            List<DrawList> createDrawList(bool alphaBlended)
            {
                var result = new List<DrawList>();
                foreach (var pipelineState in uniquePipelineStates)
                {
                    if (pipelineState.Description.Blending.Enabled != alphaBlended)
                    {
                        continue;
                    }

                    result.Add(new DrawList
                    {
                        PipelineState = pipelineState,
                        MaterialPasses = MaterialPasses
                            .Where(x => x.MeshParts.Any(y => y.PipelineState == pipelineState))
                            .ToList()
                    });
                }
                return result;
            }

            _drawListsOpaque = createDrawList(false);
            _drawListsTransparent = createDrawList(true);
        }

        public void SetMatrices(
            ref Matrix4x4 world, 
            ref Matrix4x4 view, 
            ref Matrix4x4 projection)
        {
            _meshTransformConstants.WorldViewProjection = world * view * projection;
            _meshTransformConstants.World = world;
            _meshTransformConstants.SkinningEnabled = Skinned;

            _meshTransformConstantBuffer.SetData(ref _meshTransformConstants);
        }

        private StaticBuffer CreateMaterialsBuffer(
            ContentManager contentManager,
            ResourceUploadBatch uploadBatch,
            W3dMesh w3dMesh)
        {
            var vertexMaterials = new VertexMaterial[w3dMesh.Materials.Length];

            for (var i = 0; i < w3dMesh.Materials.Length; i++)
            {
                var w3dMaterial = w3dMesh.Materials[i];
                var w3dVertexMaterial = w3dMaterial.VertexMaterialInfo;

                vertexMaterials[i] = new VertexMaterial
                {
                    Ambient = w3dVertexMaterial.Ambient.ToVector3(),
                    Diffuse = w3dVertexMaterial.Diffuse.ToVector3(),
                    Specular = w3dVertexMaterial.Specular.ToVector3(),
                    Emissive = w3dVertexMaterial.Emissive.ToVector3(),
                    Shininess = w3dVertexMaterial.Shininess,
                    Opacity = w3dVertexMaterial.Opacity,
                    TextureMappingStage0 = ConversionExtensions.CreateTextureMapping(
                        w3dVertexMaterial.Stage0Mapping,
                        w3dMaterial.MapperArgs0),
                    TextureMappingStage1 = ConversionExtensions.CreateTextureMapping(
                        w3dVertexMaterial.Stage1Mapping,
                        w3dMaterial.MapperArgs1)
                };
            }

            return AddDisposable(StaticBuffer.Create(
                contentManager.GraphicsDevice,
                uploadBatch,
                vertexMaterials,
                false));
        }

        private static Texture[] CreateTextures(
            ContentManager contentManager,
            ResourceUploadBatch uploadBatch,
            W3dMesh w3dMesh)
        {
            var numTextures = w3dMesh.Textures.Length;
            var textures = new Texture[numTextures];
            for (var i = 0; i < numTextures; i++)
            {
                var w3dTexture = w3dMesh.Textures[i];
                var w3dTextureFilePath = Path.Combine("Art", "Textures", w3dTexture.Name);
                textures[i] = contentManager.Load<Texture>(w3dTextureFilePath, uploadBatch);
            }
            return textures;
        }

        private StaticBuffer CreateVertexBuffer(
            ContentManager contentManager,
            ResourceUploadBatch uploadBatch,
            W3dMesh w3dMesh,
            bool isSkinned)
        {
            var vertices = new MeshVertex[_numVertices];

            for (var i = 0; i < _numVertices; i++)
            {
                vertices[i] = new MeshVertex
                {
                    Position = w3dMesh.Vertices[i].ToVector3(),
                    Normal = w3dMesh.Normals[i].ToVector3(),
                    BoneIndex = isSkinned
                        ? w3dMesh.Influences[i].BoneIndex
                        : 0u
                };
            }

            return AddDisposable(StaticBuffer.Create(
                contentManager.GraphicsDevice,
                uploadBatch,
                vertices,
                false));
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MeshVertex
        {
            public const int SizeInBytes = sizeof(float) * 6 + sizeof(uint);

            public Vector3 Position;
            public Vector3 Normal;
            public uint BoneIndex;
        }

        private StaticBuffer CreateIndexBuffer(
            ContentManager contentManager,
            ResourceUploadBatch uploadBatch,
            W3dMesh w3dMesh)
        {
            var indices = new ushort[_numIndices];

            var indexIndex = 0;
            foreach (var triangle in w3dMesh.Triangles)
            {
                indices[indexIndex++] = (ushort) triangle.VIndex0;
                indices[indexIndex++] = (ushort) triangle.VIndex1;
                indices[indexIndex++] = (ushort) triangle.VIndex2;
            }

            return AddDisposable(StaticBuffer.Create(
                contentManager.GraphicsDevice,
                uploadBatch,
                indices,
                false));
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MeshTransformConstants
        {
            public Matrix4x4 WorldViewProjection;
            public Matrix4x4 World;
            public bool SkinningEnabled;
        }

        public void Draw(CommandEncoder commandEncoder, bool alphaBlended)
        {
            // TODO: Use time from main game engine, don't query for it every time like this.
            var timeInSeconds = (float) System.DateTime.Now.TimeOfDay.TotalSeconds;

            void drawImpl(PipelineState pipelineState, IEnumerable<ModelMeshMaterialPass> materialPasses)
            {
                commandEncoder.SetPipelineState(pipelineState);

                commandEncoder.SetInlineConstantBuffer(1, _meshTransformConstantBuffer);
                commandEncoder.SetDescriptorSet(6, _pixelMeshDescriptorSet);

                commandEncoder.SetVertexBuffer(0, _vertexBuffer);

                foreach (var materialPass in materialPasses)
                {
                    commandEncoder.SetDescriptorSet(4, materialPass.VertexMaterialPassDescriptorSet);
                    commandEncoder.SetDescriptorSet(5, materialPass.PixelMaterialPassDescriptorSet);

                    commandEncoder.SetVertexBuffer(1, materialPass.TexCoordVertexBuffer);

                    foreach (var meshPart in materialPass.MeshParts)
                    {
                        if (meshPart.PipelineState != pipelineState)
                        {
                            continue;
                        }

                        _perDrawConstants.PrimitiveOffset = meshPart.StartIndex / 3;
                        _perDrawConstants.NumTextureStages = materialPass.NumTextureStages;
                        _perDrawConstants.AlphaTest = meshPart.AlphaTest;
                        _perDrawConstants.Texturing = meshPart.Texturing;
                        _perDrawConstants.TimeInSeconds = timeInSeconds;

                        _perDrawConstantBuffer.SetData(ref _perDrawConstants);
                        commandEncoder.SetInlineConstantBuffer(0, _perDrawConstantBuffer);

                        commandEncoder.DrawIndexed(
                            PrimitiveType.TriangleList,
                            meshPart.IndexCount,
                            IndexType.UInt16,
                            _indexBuffer,
                            meshPart.StartIndex);
                    }
                }
            }

            var drawLists = alphaBlended
                ? _drawListsTransparent
                : _drawListsOpaque;

            foreach (var drawList in drawLists)
            {
                drawImpl(drawList.PipelineState, drawList.MaterialPasses);
            }
        }

        private sealed class DrawList
        {
            public PipelineState PipelineState;
            public List<ModelMeshMaterialPass> MaterialPasses;
        }

        [StructLayout(LayoutKind.Explicit, Size = 96)]
        private struct VertexMaterial
        {
            [FieldOffset(0)]
            public Vector3 Ambient;

            [FieldOffset(12)]
            public Vector3 Diffuse;

            [FieldOffset(24)]
            public Vector3 Specular;

            [FieldOffset(36)]
            public float Shininess;

            [FieldOffset(40)]
            public Vector3 Emissive;

            [FieldOffset(52)]
            public float Opacity;

            [FieldOffset(56)]
            public TextureMapping TextureMappingStage0;

            [FieldOffset(76)]
            public TextureMapping TextureMappingStage1;
        }

        [StructLayout(LayoutKind.Explicit, Size = 20)]
        private struct PerDrawConstants
        {
            [FieldOffset(0)]
            public uint PrimitiveOffset;

            // Not actually per-draw, but we don't have a per-mesh CB.
            [FieldOffset(4)]
            public uint NumTextureStages;

            [FieldOffset(8)]
            public bool AlphaTest;

            [FieldOffset(12)]
            public bool Texturing;

            [FieldOffset(16)]
            public float TimeInSeconds;
        }
    }

    internal enum TextureMappingType : uint
    {
        Uv = 0,
        Environment = 1,
        LinearOffset = 2
    }

    [StructLayout(LayoutKind.Explicit, Size = 20)]
    internal struct TextureMapping
    {
        [FieldOffset(0)]
        public TextureMappingType MappingType;

        [FieldOffset(4)]
        public Vector2 UVPerSec;

        [FieldOffset(12)]
        public Vector2 UVScale;
    }
}
