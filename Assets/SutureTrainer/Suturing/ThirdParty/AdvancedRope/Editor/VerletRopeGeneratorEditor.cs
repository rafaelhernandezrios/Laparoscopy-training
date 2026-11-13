using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AdvancedRope.Editor
{
    [CustomEditor(typeof(VerletRopeGenerator))]
    public class VerletRopeGeneratorEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();

            Header(root);
            InitAndEnabled(root);
            ResolutionAndDimensionSettings(root);
            PhysicsParametersSettings(root);
            MeshSettings(root);
            StartNodeSettings(root);
            EndNodeSettings(root);
            MiddleNodeSettings(root);
            
            UpdateStartEndPoints(root);
            
            return root;
        }

        private void UpdateStartEndPoints(VisualElement root)
        {
            root.schedule.Execute(() =>
            {
                if (target == null) return;
        
                var rope = (VerletRopeGenerator)target;
                var isDirty = false;

                if (rope.initialPinPoints != null)
                {
                    foreach (var pin in rope.initialPinPoints)
                    {
                        if (pin.targetTransform != null)
                        {
                            if (pin.nodeIndex == 0)
                            {
                                var expectedStart = pin.targetTransform.TransformPoint(pin.localPos);
                                if (Vector3.Distance(rope.ropeStartPoint, expectedStart) > 0.001f)
                                {
                                    rope.ropeStartPoint = expectedStart;
                                    isDirty = true;
                                }
                            }
                            else if (pin.nodeIndex == -1)
                            {
                                var expectedEnd = pin.targetTransform.TransformPoint(pin.localPos);
                                if (Vector3.Distance(rope.ropeEndPoint, expectedEnd) > 0.001f)
                                {
                                    rope.ropeEndPoint = expectedEnd;
                                    isDirty = true;
                                }
                            }
                        }
                    }
                }

                if (isDirty)
                {
                    EditorUtility.SetDirty(rope);
                    serializedObject.Update();
                }
            }).Every(20);
        }

        private void Header(VisualElement root)
        {
            var titleLabel = new Label("Advanced Rope Settings")
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    fontSize = 14,
                    marginBottom = 10
                }
            };

            root.Add(titleLabel);
        }
        private void InitAndEnabled(VisualElement root)
        {
            var generalLabel = new Label("General Settings")
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    marginTop = 15
                }
            };
            
            root.Add(generalLabel);

            var initProp = serializedObject.FindProperty("initializeOnStart");
            var enabledProp = serializedObject.FindProperty("enabled");
            
            var initField = new PropertyField(initProp);
            var enabledField = new PropertyField(enabledProp);
            
            root.Add(initField);
            root.Add(enabledField);
        }
        private void ResolutionAndDimensionSettings(VisualElement root)
        {
            var foldout = new Foldout { text = "Resolution & Dimensions", value = true, style = { marginTop = 15 } };
            var foldoutLabel = foldout.Q<Label>();
            if (foldoutLabel != null) foldoutLabel.style.unityFontStyleAndWeight = FontStyle.Bold;

            var cardBox = new Box
            {
                style =
                {
                    marginTop = 5,
                    paddingRight = 10,
                    paddingLeft = 10,
                    paddingBottom = 10,
                    paddingTop = 10,
                    backgroundColor = new Color(0.15f, 0.15f, 0.15f, 0.5f),
                    borderBottomLeftRadius = 5,
                    borderTopLeftRadius = 5,
                    borderBottomRightRadius = 5,
                    borderTopRightRadius = 5
                }
            };

            var resolutionProp = serializedObject.FindProperty("ropeResolution");
            var radiusProp = serializedObject.FindProperty("ropeRadius");

            var resolutionField = new PropertyField(resolutionProp, "Rope Resolution");
            var radiusField = new PropertyField(radiusProp, "Rope Radius");

            cardBox.Add(resolutionField);
            cardBox.Add(radiusField);

            foldout.Add(cardBox);
            root.Add(foldout);
        }
        private void PhysicsParametersSettings(VisualElement root)
        {
            var foldout = new Foldout { text = "Physics Parameters", value = false, style = { marginTop = 15 } };
            var foldoutLabel = foldout.Q<Label>();
            if (foldoutLabel != null) foldoutLabel.style.unityFontStyleAndWeight = FontStyle.Bold;

            var cardBox = new Box
            {
                style =
                {
                    marginTop = 5,
                    paddingRight = 10,
                    paddingLeft = 10,
                    paddingBottom = 10,
                    paddingTop = 10,
                    backgroundColor = new Color(0.15f, 0.15f, 0.15f, 0.5f),
                    borderBottomLeftRadius = 5,
                    borderTopLeftRadius = 5,
                    borderBottomRightRadius = 5,
                    borderTopRightRadius = 5
                }
            };

            var dynLabel = new Label("Simulation Dynamics")
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    marginBottom = 5
                }
            };
            cardBox.Add(dynLabel);

            cardBox.Add(new PropertyField(serializedObject.FindProperty("gravity")));
            cardBox.Add(new PropertyField(serializedObject.FindProperty("airFriction")));
            cardBox.Add(new PropertyField(serializedObject.FindProperty("stiffness")));

            var solverLabel = new Label("Solver Precision")
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    marginTop = 15,
                    marginBottom = 5
                }
            };
            cardBox.Add(solverLabel);

            cardBox.Add(new PropertyField(serializedObject.FindProperty("constraintIterations")));

            var colLabel = new Label("Collision Settings")
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    marginTop = 15,
                    marginBottom = 5
                }
            };
            cardBox.Add(colLabel);

            var enableCollisionsProp = serializedObject.FindProperty("enableCollisions");
            var enableCollisionsField = new PropertyField(enableCollisionsProp);
            cardBox.Add(enableCollisionsField);

            var colLayerField = new PropertyField(serializedObject.FindProperty("collisionLayer"));
            var enableSelfColField = new PropertyField(serializedObject.FindProperty("enableSelfCollision"));
            var colPushField = new PropertyField(serializedObject.FindProperty("collisionPushPower"));
            var contactFricField = new PropertyField(serializedObject.FindProperty("contactFriction"));

            cardBox.Add(colLayerField);
            cardBox.Add(enableSelfColField);
            cardBox.Add(colPushField);
            cardBox.Add(contactFricField);

            void RefreshCollisionUI()
            {
                var display = enableCollisionsProp.boolValue ? DisplayStyle.Flex : DisplayStyle.None;
                colLayerField.style.display = display;
                enableSelfColField.style.display = display;
                colPushField.style.display = display;
                contactFricField.style.display = display;
            }

            enableCollisionsField.RegisterValueChangeCallback(evt =>
            {
                RefreshCollisionUI();
            });

            var rbLabel = new Label("Rigidbody Interaction")
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    marginTop = 15,
                    marginBottom = 5
                }
            };
            cardBox.Add(rbLabel);

            cardBox.Add(new PropertyField(serializedObject.FindProperty("rigidbodyInteractionForce")));

            foldout.Add(cardBox);
            root.Add(foldout);

            RefreshCollisionUI();
        }
        private void MeshSettings(VisualElement root)
        {
            var foldout = new Foldout { text = "Mesh Settings", value = false, style = { marginTop = 15 } };
            var foldoutLabel = foldout.Q<Label>();
            if (foldoutLabel != null) foldoutLabel.style.unityFontStyleAndWeight = FontStyle.Bold;

            var cardBox = new Box
            {
                style =
                {
                    marginTop = 5,
                    paddingRight = 10,
                    paddingLeft = 10,
                    paddingBottom = 10,
                    paddingTop = 10,
                    backgroundColor = new Color(0.15f, 0.15f, 0.15f, 0.5f),
                    borderBottomLeftRadius = 5,
                    borderTopLeftRadius = 5,
                    borderBottomRightRadius = 5,
                    borderTopRightRadius = 5
                }
            };

            var radialSegmentsProp = serializedObject.FindProperty("meshRadialSegments");
            var textureTilingProp = serializedObject.FindProperty("meshTextureTiling");

            var radialSegmentsField = new PropertyField(radialSegmentsProp, "Radial Segments");
            var textureTilingField = new PropertyField(textureTilingProp, "Texture Tiling");

            cardBox.Add(radialSegmentsField);
            cardBox.Add(textureTilingField);

            foldout.Add(cardBox);
            root.Add(foldout);
        }
        private void StartNodeSettings(VisualElement root)
        {
            var foldout = new Foldout { text = "Start Node Setup", value = true, style = { marginTop = 15 } };
            var foldoutLabel = foldout.Q<Label>();
            if (foldoutLabel != null) foldoutLabel.style.unityFontStyleAndWeight = FontStyle.Bold;

            var cardBox = new Box
            {
                style =
                {
                    marginTop = 5,
                    paddingRight = 10,
                    paddingLeft = 10,
                    paddingBottom = 10,
                    paddingTop = 10,
                    backgroundColor = new Color(0.15f, 0.15f, 0.15f, 0.5f),
                    borderBottomLeftRadius = 5,
                    borderTopLeftRadius = 5,
                    borderBottomRightRadius = 5,
                    borderTopRightRadius = 5
                }
            };

            var startPointProp = serializedObject.FindProperty("ropeStartPoint");
            var connectedBodyProp = serializedObject.FindProperty("connectedBodyStart");

            var connectedBodyField = new PropertyField(connectedBodyProp, "Coupled Rigidbody");
            var startPointField = new PropertyField(startPointProp, "Spawn Position");
            var pinTargetField = new ObjectField("Pin Target (Optional)") { objectType = typeof(Transform), allowSceneObjects = true };
            var pinOffsetField = new Vector3Field("Pin Local Offset");

            System.Action<Transform, Vector3> SetStartPinData = (Transform targetTransform, Vector3 localPos) =>
            {
                serializedObject.Update(); 
                
                var listProp = serializedObject.FindProperty("initialPinPoints");

                var idx = -1;
                for (var i = 0; i < listProp.arraySize; i++)
                {
                    if (listProp.GetArrayElementAtIndex(i).FindPropertyRelative("nodeIndex").intValue == 0)
                    {
                        idx = i;
                        break;
                    }
                }

                if (targetTransform == null)
                {
                    if (idx != -1)
                    {
                        listProp.GetArrayElementAtIndex(idx).FindPropertyRelative("targetTransform").objectReferenceValue = null;
                        listProp.DeleteArrayElementAtIndex(idx);
                    }
                }
                else
                {
                    if (idx == -1)
                    {
                        listProp.InsertArrayElementAtIndex(listProp.arraySize);
                        idx = listProp.arraySize - 1;
                    }

                    var elementProp = listProp.GetArrayElementAtIndex(idx);
                    elementProp.FindPropertyRelative("nodeIndex").intValue = 0;
                    elementProp.FindPropertyRelative("targetTransform").objectReferenceValue = targetTransform;
                    elementProp.FindPropertyRelative("localPos").vector3Value = localPos;
                }

                serializedObject.ApplyModifiedProperties();
            };

            void RefreshUIState()
            {
                serializedObject.Update();
                var hasRb = connectedBodyProp.objectReferenceValue != null;

                var listProp = serializedObject.FindProperty("initialPinPoints");
                var hasPin = false;

                for (var i = 0; i < listProp.arraySize; i++)
                {
                    var element = listProp.GetArrayElementAtIndex(i);
                    if (element.FindPropertyRelative("nodeIndex").intValue == 0)
                    {
                        var targetObj = element.FindPropertyRelative("targetTransform").objectReferenceValue;
                        if (targetObj != null)
                        {
                            hasPin = true;
                            pinTargetField.SetValueWithoutNotify(targetObj);
                            pinOffsetField.SetValueWithoutNotify(element.FindPropertyRelative("localPos").vector3Value);
                        }

                        break;
                    }
                }

                if (!hasPin)
                {
                    pinTargetField.SetValueWithoutNotify(null);
                    pinOffsetField.SetValueWithoutNotify(Vector3.zero);
                }

                startPointField.style.display = (hasRb || hasPin) ? DisplayStyle.None : DisplayStyle.Flex;
                pinTargetField.style.display = hasRb ? DisplayStyle.None : DisplayStyle.Flex;
                pinOffsetField.style.display = (hasRb || hasPin) ? DisplayStyle.Flex : DisplayStyle.None;
            }

            pinTargetField.RegisterValueChangedCallback(evt =>
            {
                var newTarget = evt.newValue as Transform;
                var relativeOffset = Vector3.zero;

                if (newTarget != null)
                {
                    serializedObject.Update();
                    var currentStartPoint = serializedObject.FindProperty("ropeStartPoint").vector3Value;
                    relativeOffset = newTarget.InverseTransformPoint(currentStartPoint);
                }

                SetStartPinData(newTarget, relativeOffset);
                RefreshUIState();
            });

            pinOffsetField.RegisterValueChangedCallback(evt =>
            {
                var currentTarget = pinTargetField.value as Transform;
                if (currentTarget == null && connectedBodyProp.objectReferenceValue != null)
                    currentTarget = (connectedBodyProp.objectReferenceValue as Rigidbody)?.transform;

                if (currentTarget != null)
                {
                    SetStartPinData(currentTarget, evt.newValue);
                    
                    serializedObject.Update();
                    serializedObject.FindProperty("ropeStartPoint").vector3Value = currentTarget.TransformPoint(evt.newValue);
                    serializedObject.ApplyModifiedProperties();
                }
            });

            root.TrackPropertyValue(connectedBodyProp, prop =>
            {
                var newRb = prop.objectReferenceValue as Rigidbody;
                if (newRb != null)
                {
                    serializedObject.Update();
                    var currentStartPoint = serializedObject.FindProperty("ropeStartPoint").vector3Value;
                    var relativeOffset = newRb.transform.InverseTransformPoint(currentStartPoint);
                    SetStartPinData(newRb.transform, relativeOffset);
                }
                else
                {
                    SetStartPinData(null, Vector3.zero);
                }
                RefreshUIState();
            });

            cardBox.Add(connectedBodyField);
            cardBox.Add(startPointField);
            cardBox.Add(pinTargetField);
            cardBox.Add(pinOffsetField);
            foldout.Add(cardBox);
            root.Add(foldout);

            RefreshUIState();
        }
        private void EndNodeSettings(VisualElement root)
        {
            var foldout = new Foldout { text = "End Node Setup", value = true, style = { marginTop = 15 } };
            var foldoutLabel = foldout.Q<Label>();
            if (foldoutLabel != null) foldoutLabel.style.unityFontStyleAndWeight = FontStyle.Bold;

            var cardBox = new Box
            {
                style =
                {
                    marginTop = 5,
                    paddingRight = 10,
                    paddingLeft = 10,
                    paddingBottom = 10,
                    paddingTop = 10,
                    backgroundColor = new Color(0.15f, 0.15f, 0.15f, 0.5f),
                    borderBottomLeftRadius = 5,
                    borderTopLeftRadius = 5,
                    borderBottomRightRadius = 5,
                    borderTopRightRadius = 5
                }
            };

            var endPointProp = serializedObject.FindProperty("ropeEndPoint");
            var connectedBodyProp = serializedObject.FindProperty("connectedBodyEnd");

            var connectedBodyField = new PropertyField(connectedBodyProp, "Coupled Rigidbody");
            var endPointField = new PropertyField(endPointProp, "Spawn Position");
            var pinTargetField = new ObjectField("Pin Target (Optional)") { objectType = typeof(Transform), allowSceneObjects = true };
            var pinOffsetField = new Vector3Field("Pin Local Offset");

            System.Action<Transform, Vector3> SetEndPinData = (Transform targetTransform, Vector3 localPos) =>
            {
                serializedObject.Update(); 
                
                var listProp = serializedObject.FindProperty("initialPinPoints");

                var idx = -1;
                for (var i = 0; i < listProp.arraySize; i++)
                {
                    if (listProp.GetArrayElementAtIndex(i).FindPropertyRelative("nodeIndex").intValue == -1)
                    {
                        idx = i;
                        break;
                    }
                }

                if (targetTransform == null)
                {
                    if (idx != -1)
                    {
                        listProp.GetArrayElementAtIndex(idx).FindPropertyRelative("targetTransform").objectReferenceValue = null;
                        listProp.DeleteArrayElementAtIndex(idx);
                    }
                }
                else
                {
                    if (idx == -1)
                    {
                        listProp.InsertArrayElementAtIndex(listProp.arraySize);
                        idx = listProp.arraySize - 1;
                    }

                    var elementProp = listProp.GetArrayElementAtIndex(idx);
                    elementProp.FindPropertyRelative("nodeIndex").intValue = -1;
                    elementProp.FindPropertyRelative("targetTransform").objectReferenceValue = targetTransform;
                    elementProp.FindPropertyRelative("localPos").vector3Value = localPos;
                }

                serializedObject.ApplyModifiedProperties();
            };

            void RefreshUIState()
            {
                serializedObject.Update();
                var hasRb = connectedBodyProp.objectReferenceValue != null;

                var listProp = serializedObject.FindProperty("initialPinPoints");
                var hasPin = false;

                for (var i = 0; i < listProp.arraySize; i++)
                {
                    var element = listProp.GetArrayElementAtIndex(i);
                    if (element.FindPropertyRelative("nodeIndex").intValue == -1)
                    {
                        var targetObj = element.FindPropertyRelative("targetTransform").objectReferenceValue;
                        if (targetObj != null)
                        {
                            hasPin = true;
                            pinTargetField.SetValueWithoutNotify(targetObj);
                            pinOffsetField.SetValueWithoutNotify(element.FindPropertyRelative("localPos").vector3Value);
                        }

                        break;
                    }
                }

                if (!hasPin)
                {
                    pinTargetField.SetValueWithoutNotify(null);
                    pinOffsetField.SetValueWithoutNotify(Vector3.zero);
                }

                endPointField.style.display = (hasRb || hasPin) ? DisplayStyle.None : DisplayStyle.Flex;
                pinTargetField.style.display = hasRb ? DisplayStyle.None : DisplayStyle.Flex;
                pinOffsetField.style.display = (hasRb || hasPin) ? DisplayStyle.Flex : DisplayStyle.None;
            }

            pinTargetField.RegisterValueChangedCallback(evt =>
            {
                var newTarget = evt.newValue as Transform;
                var relativeOffset = Vector3.zero;

                if (newTarget != null)
                {
                    serializedObject.Update();
                    var currentEndPoint = serializedObject.FindProperty("ropeEndPoint").vector3Value;
                    relativeOffset = newTarget.InverseTransformPoint(currentEndPoint);
                }

                SetEndPinData(newTarget, relativeOffset);
                RefreshUIState();
            });

            pinOffsetField.RegisterValueChangedCallback(evt =>
            {
                var currentTarget = pinTargetField.value as Transform;
                if (currentTarget == null && connectedBodyProp.objectReferenceValue != null)
                    currentTarget = (connectedBodyProp.objectReferenceValue as Rigidbody)?.transform;

                if (currentTarget != null)
                {
                    SetEndPinData(currentTarget, evt.newValue);
                    
                    serializedObject.Update();
                    serializedObject.FindProperty("ropeEndPoint").vector3Value = currentTarget.TransformPoint(evt.newValue);
                    serializedObject.ApplyModifiedProperties();
                }
            });

            root.TrackPropertyValue(connectedBodyProp, prop =>
            {
                var newRb = prop.objectReferenceValue as Rigidbody;
                if (newRb != null)
                {
                    serializedObject.Update();
                    var currentEndPoint = serializedObject.FindProperty("ropeEndPoint").vector3Value;
                    var relativeOffset = newRb.transform.InverseTransformPoint(currentEndPoint);
                    SetEndPinData(newRb.transform, relativeOffset);
                }
                else
                {
                    SetEndPinData(null, Vector3.zero);
                }
                RefreshUIState();
            });

            cardBox.Add(connectedBodyField);
            cardBox.Add(endPointField);
            cardBox.Add(pinTargetField);
            cardBox.Add(pinOffsetField);
            foldout.Add(cardBox);
            root.Add(foldout);

            RefreshUIState();
        }
        private void MiddleNodeSettings(VisualElement root)
        {
            var foldout = new Foldout { text = "Middle Nodes Setup", value = false, style = { marginTop = 15 } };
            var foldoutLabel = foldout.Q<Label>();
            if (foldoutLabel != null) foldoutLabel.style.unityFontStyleAndWeight = FontStyle.Bold;

            var cardBox = new Box
            {
                style =
                {
                    marginTop = 5,
                    paddingRight = 10,
                    paddingLeft = 10,
                    paddingBottom = 10,
                    paddingTop = 10,
                    backgroundColor = new Color(0.15f, 0.15f, 0.15f, 0.5f),
                    borderBottomLeftRadius = 5,
                    borderTopLeftRadius = 5,
                    borderBottomRightRadius = 5,
                    borderTopRightRadius = 5
                }
            };

            var listContainer = new VisualElement();
            cardBox.Add(listContainer);

            void RefreshMiddlePins()
            {
                listContainer.Clear();
                serializedObject.Update();
                var listProp = serializedObject.FindProperty("initialPinPoints");

                for (var i = 0; i < listProp.arraySize; i++)
                {
                    var index = i;
                    var elementProp = listProp.GetArrayElementAtIndex(index);
                    var nodeIndexProp = elementProp.FindPropertyRelative("nodeIndex");

                    if (nodeIndexProp.intValue != 0 && nodeIndexProp.intValue != -1)
                    {
                        var rowBox = new Box
                        {
                            style =
                            {
                                flexDirection = FlexDirection.Column,
                                marginBottom = 5,
                                paddingBottom = 5,
                                paddingTop = 5,
                                paddingLeft = 5,
                                paddingRight = 5,
                                backgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.3f),
                                borderBottomLeftRadius = 3,
                                borderTopLeftRadius = 3,
                                borderBottomRightRadius = 3,
                                borderTopRightRadius = 3
                            }
                        };

                        var headerRow = new VisualElement 
                        { 
                            style = 
                            { 
                                flexDirection = FlexDirection.Row, 
                                justifyContent = Justify.SpaceBetween,
                                marginBottom = 5
                            } 
                        };
                        
                        var indexField = new IntegerField("Node Index") { value = nodeIndexProp.intValue };
                        indexField.style.flexGrow = 1;
                        indexField.style.marginRight = 5;
                        indexField.RegisterValueChangedCallback(evt =>
                        {
                            var newVal = evt.newValue;
                            if (newVal == 0) newVal = 1;
                            if (newVal == -1) newVal = -2;
                            
                            serializedObject.Update();
                            listProp.GetArrayElementAtIndex(index).FindPropertyRelative("nodeIndex").intValue = newVal;
                            serializedObject.ApplyModifiedProperties();
                        });

                        var removeBtn = new Button(() =>
                        {
                            serializedObject.Update();
                            listProp.GetArrayElementAtIndex(index).FindPropertyRelative("targetTransform").objectReferenceValue = null;
                            listProp.DeleteArrayElementAtIndex(index);
                            serializedObject.ApplyModifiedProperties();
                            RefreshMiddlePins();
                        }) 
                        { 
                            text = "X", 
                            style = 
                            { 
                                width = 20, 
                                backgroundColor = new Color(0.7f, 0.2f, 0.2f, 1f),
                                color = Color.white,
                                unityFontStyleAndWeight = FontStyle.Bold
                            } 
                        };

                        headerRow.Add(indexField);
                        headerRow.Add(removeBtn);

                        var targetField = new ObjectField("Target") { objectType = typeof(Transform), allowSceneObjects = true };
                        targetField.value = elementProp.FindPropertyRelative("targetTransform").objectReferenceValue;
                        targetField.RegisterValueChangedCallback(evt =>
                        {
                            serializedObject.Update();
                            listProp.GetArrayElementAtIndex(index).FindPropertyRelative("targetTransform").objectReferenceValue = evt.newValue;
                            serializedObject.ApplyModifiedProperties();
                        });

                        var offsetField = new Vector3Field("Offset") { value = elementProp.FindPropertyRelative("localPos").vector3Value };
                        offsetField.RegisterValueChangedCallback(evt =>
                        {
                            serializedObject.Update();
                            listProp.GetArrayElementAtIndex(index).FindPropertyRelative("localPos").vector3Value = evt.newValue;
                            serializedObject.ApplyModifiedProperties();
                        });

                        rowBox.Add(headerRow);
                        rowBox.Add(targetField);
                        rowBox.Add(offsetField);

                        listContainer.Add(rowBox);
                    }
                }
            }

            var addButton = new Button(() =>
            {
                serializedObject.Update();
                var listProp = serializedObject.FindProperty("initialPinPoints");
                listProp.InsertArrayElementAtIndex(listProp.arraySize);
                var newElem = listProp.GetArrayElementAtIndex(listProp.arraySize - 1);
                newElem.FindPropertyRelative("nodeIndex").intValue = 1;
                newElem.FindPropertyRelative("targetTransform").objectReferenceValue = null;
                newElem.FindPropertyRelative("localPos").vector3Value = Vector3.zero;
                serializedObject.ApplyModifiedProperties();
                RefreshMiddlePins();
            })
            {
                text = "Add Middle Pin",
                style = 
                { 
                    marginTop = 10,
                    paddingBottom = 5,
                    paddingTop = 5
                }
            };
            
            cardBox.Add(addButton);
            foldout.Add(cardBox);
            root.Add(foldout);

            RefreshMiddlePins();
        }
    }
}