using GrxCAD.ApplicationServices;
using GrxCAD.DatabaseServices;
using GrxCAD.EditorInput;
using GrxCAD.Geometry;
using GrxCAD.Runtime;
using System.Collections.Generic;

namespace BlockFromCubePlugin16
{
    public class BlockCreator
    {
        [CommandMethod("CreateBlockFromCube")]
        public static void CreateBlockFromCube()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Database db = doc.Database;
            Editor ed = doc.Editor;
            
            // 1. Select the boundary cube
            PromptEntityOptions peo = new PromptEntityOptions("\nSelect the boundary cube: ");
            peo.SetRejectMessage("\nSelection must be a 3D Solid cube or prism.");
            peo.AddAllowedClass(typeof(Solid3d), true);
            PromptEntityResult per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK) return;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    // 2. Find text and get block name
                    Solid3d boundaryCube = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as Solid3d;
                    if (boundaryCube != null)
                    {
                        Point3d minPoint = boundaryCube.GeometricExtents.MinPoint;
                        string blockName = string.Empty;
                        ObjectId titleTextId = ObjectId.Null;

                        TypedValue[] filterValues = new TypedValue[]
                        {
                            new TypedValue((int)DxfCode.Start, "TEXT"),
                            new TypedValue((int)DxfCode.LayerName, "block_title")
                        };
                        SelectionFilter textFilter = new SelectionFilter(filterValues);
                        PromptSelectionResult psrText = ed.SelectAll(textFilter);

                        if (psrText.Status == PromptStatus.OK)
                        {
                            foreach (ObjectId id in psrText.Value.GetObjectIds())
                            {
                                DBText text = tr.GetObject(id, OpenMode.ForRead) as DBText;
                                if (text != null && text.Position.IsEqualTo(minPoint, new Tolerance(1e-6, 1e-6)))
                                {
                                    blockName = text.TextString;
                                    titleTextId = id;
                                    break;
                                }
                            }
                        }

                        if (string.IsNullOrEmpty(blockName))
                        {
                            ed.WriteMessage("\nError: Could not find title text on layer 'block_title' at the cube's corner.");
                            return;
                        }
                    
                        BlockTable bt = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                        if (bt != null && bt.Has(blockName))
                        {
                            ed.WriteMessage($"\nError: A block named '{blockName}' already exists.");
                            return;
                        }
                    
                        // 3. Find all entities inside the cube
                        Point3d maxPoint = boundaryCube.GeometricExtents.MaxPoint;
                        PromptSelectionResult psrContents = ed.SelectCrossingWindow(minPoint, maxPoint);
                    
                        // 4. Create the block definition
                        if (bt != null)
                        {
                            bt.UpgradeOpen();
                            BlockTableRecord btr = new BlockTableRecord
                            {
                                Name = blockName,
                                Origin = minPoint,
                            };
                            ObjectId btrId = bt.Add(btr);
                            tr.AddNewlyCreatedDBObject(btr, true);

                            List<ObjectId> originalObjectsToDelete = new List<ObjectId>
                            {
                                per.ObjectId,
                                titleTextId
                            };

                            // Add clones of all objects to the block definition
                            var entitiesToClone = new List<ObjectId> { per.ObjectId };
                            if (psrContents.Status == PromptStatus.OK)
                            {
                                foreach (ObjectId id in psrContents.Value.GetObjectIds())
                                {
                                    if (id != per.ObjectId && id != titleTextId)
                                    {
                                        entitiesToClone.Add(id);
                                        originalObjectsToDelete.Add(id);
                                    }
                                }
                            }

                            foreach (ObjectId entId in entitiesToClone)
                            {
                                Entity ent = (Entity)tr.GetObject(entId, OpenMode.ForRead);
                                Entity entClone = (Entity)ent.Clone();
                                btr.AppendEntity(entClone);
                                tr.AddNewlyCreatedDBObject(entClone, true);
                            }

                            // 5. Insert block and clean up
                            BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace],
                                OpenMode.ForWrite);
                            BlockReference br = new BlockReference(minPoint, btrId);
                            ms.AppendEntity(br);
                            tr.AddNewlyCreatedDBObject(br, true);

                            foreach (ObjectId idToErase in originalObjectsToDelete)
                            {
                                DBObject obj = tr.GetObject(idToErase, OpenMode.ForWrite);
                                obj.Erase();
                            }
                        }

                        ed.WriteMessage($"\nBlock '{blockName}' created successfully.");
                    }

                    tr.Commit();
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\nAn error occured during creating the block: {ex.Message}");
                    tr.Abort();
                }
            }
        }
    }
}