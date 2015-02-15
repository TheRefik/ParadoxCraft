﻿using ParadoxCraft.Blocks;
using ParadoxCraft.Helpers;
using SiliconStudio.Core.Mathematics;
using SiliconStudio.Paradox.Effects;
using SiliconStudio.Paradox.Engine;
using SiliconStudio.Paradox.EntityModel;
using SiliconStudio.Paradox.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Buffer = SiliconStudio.Paradox.Graphics.Buffer;

namespace ParadoxCraft.Terrain
{
    /// <summary>
    /// Terrain entity helper
    /// </summary>
    public class GraphicalTerrain
    {
        /// <summary>
        /// Max number of blocks the entity can support
        /// </summary>
        public const int MaxBlockCount = 32 * 0x1000;

        /// <summary>
        /// Vertex buffer
        /// </summary>
        private Buffer TerrainVertexBuffer { get; set; }

        /// <summary>
        /// Index buffer
        /// </summary>
        private Buffer TerrainIndexBuffer { get; set; }

        /// <summary>
        /// GraphicsDevice of the current game
        /// </summary>
        private GraphicsDevice GraphicsDevice { get; set; }

        /// <summary>
        /// Resulting Entity for this instance
        /// </summary>
        private Entity TerrainEntity { get; set; }

        /// <summary>
        /// List of blocks to be drawn
        /// </summary>
        private Dictionary<Point3, List<GraphicalBlock>> Blocks { get; set; }

        /// <summary>
        /// Locker for cross threaded block adding/removing
        /// </summary>
        private object BlockLocker = new object();


        private bool PendingChanges = false;

        /// <summary>
        /// Creates a new instance of <see cref="GraphicalTerrain"/>
        /// </summary>
        /// <param name="device">GraphicsDevice of the current game to draw on</param>
        /// <param name="material">Terrain material</param>
        public GraphicalTerrain(GraphicsDevice device, Material material)
        {
            // Initilize variables
            GraphicsDevice = device;
            Blocks = new Dictionary<Point3, List<GraphicalBlock>>();

            int vertexBufferSize = VertexTerrain.Layout.VertexStride * 4 * MaxBlockCount * 6; //A side has 4 vertice, a block has 6 sides
            TerrainVertexBuffer = Buffer.Vertex.New(GraphicsDevice, vertexBufferSize, GraphicsResourceUsage.Dynamic);

            int indexBufferSize = sizeof(int) * 6 * MaxBlockCount * 6; //A side has 6 vertice, a block has 6 sides
            TerrainIndexBuffer = Buffer.Index.New(GraphicsDevice, indexBufferSize, GraphicsResourceUsage.Dynamic);

            // Create the terrain entity
            var model = new Model();
            model.Add(new Mesh { 
                Draw = new MeshDraw
                {
                    PrimitiveType = PrimitiveType.TriangleList,
                    VertexBuffers = new[] { new VertexBufferBinding(TerrainVertexBuffer, VertexTerrain.Layout, TerrainVertexBuffer.ElementCount) },
                    IndexBuffer = new IndexBufferBinding(TerrainIndexBuffer, true, TerrainIndexBuffer.ElementCount)
                },
                Material = material
            });
            TerrainEntity = new Entity();
            TerrainEntity.Add(ModelComponent.Key, new ModelComponent { Model = model });
        }

        /// <summary>
        /// Adds a block to be drawn
        /// </summary>
        /// <remarks>
        /// Threadsafe
        /// </remarks>
        public void AddBlock(Point3 chunkPos, GraphicalBlock toAdd)
        {
            if (MaxBlockCount <= Blocks.Count) 
                return; //Emergency overflow check, else we might end up writing corrupt memory
            lock (BlockLocker)
            {
                if (!Blocks.ContainsKey(chunkPos))
                    Blocks.Add(chunkPos, new List<GraphicalBlock>());
                Blocks[chunkPos].Add(toAdd);
            }
            PendingChanges = true;
        }

        /// <summary>
        /// Removes a chunk from the draw queue
        /// </summary>
        public void PurgeChunks(List<Point3> toRemove)
        {
            lock (BlockLocker)
            {
                foreach (Point3 chunkPos in toRemove)
                    Blocks.Remove(chunkPos);
            }
            if (toRemove.Count > 0)
                PendingChanges = true;
        }

        /// <summary>
        /// Creates the indices and vertices
        /// </summary>
        public void Build()
        {
            if (!PendingChanges) return;
            lock (BlockLocker)
            {
                GenerateVertices();
                int drawcount = GenerateIndices();
                TerrainEntity.Get<ModelComponent>(ModelComponent.Key).Model.Meshes[0].Draw.DrawCount = drawcount;
                PendingChanges = false;
            }
        }

        /// <summary>
        /// Creates the indices
        /// </summary>
        /// <returns>Amount of indices to draw</returns>
        private unsafe int GenerateIndices()
        {
            MappedResource indexMap = GraphicsDevice.MapSubresource(TerrainIndexBuffer, 0, MapMode.WriteDiscard);
            var indexBuffer = (int*)indexMap.DataBox.DataPointer;

            int index = 0,
                sideIndex = 0;
            foreach (var chunk in Blocks)
                foreach (GraphicalBlock block in chunk.Value)
                {
                    BlockSides sides = block.Sides;
                    while (sides > 0)
                    {
                        sides &= (sides - 1);

                        indexBuffer[index++] = sideIndex + 0;
                        indexBuffer[index++] = sideIndex + 1;
                        indexBuffer[index++] = sideIndex + 2;
                        indexBuffer[index++] = sideIndex + 2;
                        indexBuffer[index++] = sideIndex + 3;
                        indexBuffer[index++] = sideIndex + 0;

                        sideIndex += 4;
                    }
                }

            GraphicsDevice.UnmapSubresource(indexMap);

            return index;
        }

        /// <summary>
        /// Creates the vertices
        /// </summary>
        private unsafe void GenerateVertices()
        {
            MappedResource vertexMap = GraphicsDevice.MapSubresource(TerrainVertexBuffer, 0, MapMode.WriteDiscard);
            var vertexBuffer = (VertexTerrain*)vertexMap.DataBox.DataPointer;

            int i = 0;

            // Texture UV coordinates
            Half2
                textureTopLeft = new Half2(0, 1),
                textureTopRight = new Half2(1, 1),
                textureBtmLeft = new Half2(0, 0),
                textureBtmRight = new Half2(1, 0);

            // Size vector and corner positions
            Vector3 
                corner1 = new Vector3(0, 1, 1),
                corner2 = new Vector3(0, 1, 0),
                corner3 = new Vector3(1, 1, 1),
                corner4 = new Vector3(1, 1, 0),
                corner5 = new Vector3(0, 0, 1),
                corner6 = new Vector3(0, 0, 0),
                corner7 = new Vector3(1, 0, 1),
                corner8 = new Vector3(1, 0, 0);


            foreach (var chunk in Blocks)
                foreach (GraphicalBlock block in chunk.Value)
                {
                    #region Top
                    if (block.HasSide(BlockSides.Top))
                    {
                        Vector3
                            cornerTopLeft = block.Position + corner1,
                            cornerBtmLeft = block.Position + corner2,
                            cornerTopRight = block.Position + corner3,
                            cornerBtmRight = block.Position + corner4;
                        Half4
                            normal = new Half4((Half)0, (Half)1, (Half)0, (Half)0);

                        vertexBuffer[i++] = new VertexTerrain(cornerTopRight, normal, textureBtmLeft, 0);
                        vertexBuffer[i++] = new VertexTerrain(cornerTopLeft, normal, textureBtmRight, 0);
                        vertexBuffer[i++] = new VertexTerrain(cornerBtmLeft, normal, textureTopRight, 0);
                        vertexBuffer[i++] = new VertexTerrain(cornerBtmRight, normal, textureTopLeft, 0);
                    }
                    #endregion

                    #region Bottom
                    if (block.HasSide(BlockSides.Bottom))
                    {
                        Vector3
                            cornerTopLeft = block.Position + corner5,
                            cornerBtmLeft = block.Position + corner6,
                            cornerTopRight = block.Position + corner7,
                            cornerBtmRight = block.Position + corner8;
                        Half4
                            normal = new Half4((Half)0, (Half)(-1), (Half)0, (Half)0);

                        vertexBuffer[i++] = new VertexTerrain(cornerBtmLeft, normal, textureBtmLeft, 0);
                        vertexBuffer[i++] = new VertexTerrain(cornerTopLeft, normal, textureTopLeft, 0);
                        vertexBuffer[i++] = new VertexTerrain(cornerTopRight, normal, textureTopRight, 0);
                        vertexBuffer[i++] = new VertexTerrain(cornerBtmRight, normal, textureBtmRight, 0);
                    }
                    #endregion

                    #region Front
                    if (block.HasSide(BlockSides.Front))
                    {
                        Vector3
                            cornerTopLeft = block.Position + corner3,
                            cornerBtmLeft = block.Position + corner7,
                            cornerTopRight = block.Position + corner1,
                            cornerBtmRight = block.Position + corner5;
                        Half4
                            normal = new Half4((Half)0, (Half)0, (Half)1, (Half)0);

                        vertexBuffer[i++] = new VertexTerrain(cornerTopRight, normal, textureBtmLeft, 0);
                        vertexBuffer[i++] = new VertexTerrain(cornerTopLeft, normal, textureBtmRight, 0);
                        vertexBuffer[i++] = new VertexTerrain(cornerBtmLeft, normal, textureTopRight, 0);
                        vertexBuffer[i++] = new VertexTerrain(cornerBtmRight, normal, textureTopLeft, 0);
                    }
                    #endregion

                    #region Back
                    if (block.HasSide(BlockSides.Back))
                    {
                        Vector3
                            cornerTopLeft = block.Position + corner4,
                            cornerBtmLeft = block.Position + corner8,
                            cornerTopRight = block.Position + corner2,
                            cornerBtmRight = block.Position + corner6;
                        Half4
                            normal = new Half4((Half)0, (Half)0, (Half)(-1), (Half)0);

                        vertexBuffer[i++] = new VertexTerrain(cornerBtmLeft, normal, textureBtmLeft, 0);
                        vertexBuffer[i++] = new VertexTerrain(cornerTopLeft, normal, textureTopLeft, 0);
                        vertexBuffer[i++] = new VertexTerrain(cornerTopRight, normal, textureTopRight, 0);
                        vertexBuffer[i++] = new VertexTerrain(cornerBtmRight, normal, textureBtmRight, 0);
                    }
                    #endregion

                    #region Left
                    if (block.HasSide(BlockSides.Left))
                    {
                        Vector3
                            cornerTopLeft = block.Position + corner1,
                            cornerBtmLeft = block.Position + corner5,
                            cornerTopRight = block.Position + corner2,
                            cornerBtmRight = block.Position + corner6;
                        Half4
                            normal = new Half4((Half)1, (Half)0, (Half)0, (Half)0);

                        vertexBuffer[i++] = new VertexTerrain(cornerTopRight, normal, textureBtmLeft, 0);
                        vertexBuffer[i++] = new VertexTerrain(cornerTopLeft, normal, textureBtmRight, 0);
                        vertexBuffer[i++] = new VertexTerrain(cornerBtmLeft, normal, textureTopRight, 0);
                        vertexBuffer[i++] = new VertexTerrain(cornerBtmRight, normal, textureTopLeft, 0);
                    }
                    #endregion

                    #region Right
                    if (block.HasSide(BlockSides.Right))
                    {
                        Vector3
                            cornerTopLeft = block.Position + corner3,
                            cornerBtmLeft = block.Position + corner7,
                            cornerTopRight = block.Position + corner4,
                            cornerBtmRight = block.Position + corner8;
                        Half4
                            normal = new Half4((Half)(-1), (Half)0, (Half)0, (Half)0);

                        vertexBuffer[i++] = new VertexTerrain(cornerBtmLeft, normal, textureBtmLeft, 0);
                        vertexBuffer[i++] = new VertexTerrain(cornerTopLeft, normal, textureTopLeft, 0);
                        vertexBuffer[i++] = new VertexTerrain(cornerTopRight, normal, textureTopRight, 0);
                        vertexBuffer[i++] = new VertexTerrain(cornerBtmRight, normal, textureBtmRight, 0);
                    }
                    #endregion
                }

            GraphicsDevice.UnmapSubresource(vertexMap);
        }

        /// <summary>
        /// Cast operator so we can add this instance 'as entity' to the pipeline
        /// </summary>
        public static implicit operator Entity(GraphicalTerrain terrain)
        {
            return terrain.TerrainEntity;
        }
    }
}