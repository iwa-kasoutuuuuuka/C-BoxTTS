import onnx
import sys
import os

def patch_model(model_path):
    print(f"Loading model: {model_path}")
    if not os.path.exists(model_path):
        print("Error: File not found")
        return
        
    model = onnx.load(model_path, load_external_data=False)
    patched = False
    
    for node in model.graph.node:
        if node.op_type == 'GroupQueryAttention' and len(node.input) == 11:
            # 11 inputs -> 9 inputs (Remove K_Bias, V_Bias)
            # The indices are 9 and 10
            # Important: delete 10 first then 9
            node.input.pop(10)
            node.input.pop(9)
            patched = True
            
    if patched:
        onnx.save(model, model_path)
        print("PATCH SUCCESS: GQA nodes converted from 11 to 9 inputs.")
    else:
        print("NO PATCH NEEDED: No 11-input GQA nodes found.")

if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("Usage: python patch_onnx.py <path_to_onnx>")
    else:
        patch_model(sys.argv[1])
