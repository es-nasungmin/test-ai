import sys

def analyze_braces(filename):
    with open(filename, 'r') as f:
        content = f.read()

    stack = []
    lines = content.splitlines()
    
    in_string = None
    in_multiline_comment = False
    
    suspicious_points = []
    
    for line_num, line in enumerate(lines, 1):
        i = 0
        while i < len(line):
            char = line[i]
            
            if in_multiline_comment:
                if line[i:i+2] == '*/':
                    in_multiline_comment = False
                    i += 1
            elif in_string:
                if char == '\\':
                    i += 1
                elif char == in_string:
                    in_string = None
            else:
                if line[i:i+2] == '//':
                    break
                elif line[i:i+2] == '/*':
                    in_multiline_comment = True
                    i += 1
                elif char in ('"', "'", '`'):
                    in_string = char
                elif char == '{':
                    stack.append((line_num, i))
                elif char == '}':
                    if stack:
                        stack.pop()
                    else:
                        print(f"Unmatched '}}' at line {line_num}, col {i}")
            i += 1
            
    if stack:
        print("Unmatched '{' found at:")
        for line_num, col in stack:
            print(f"Line {line_num}, Col {col}: {lines[line_num-1].strip()}")
    else:
        print("All braces matched (according to simple parser).")

if __name__ == "__main__":
    analyze_braces(sys.argv[1])
