using Glslang.NET;
using GlslangProgram = Glslang.NET.Program;

string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
string shaderDir = Path.Combine(root, "src", "AstroCraft.Client", "Shaders");

Compile(Path.Combine(shaderDir, "shader.vert"), ShaderStage.Vertex);
Compile(Path.Combine(shaderDir, "shader.frag"), ShaderStage.Fragment);

static void Compile(string sourcePath, ShaderStage stage)
{
    string source = File.ReadAllText(sourcePath);
    string outputPath = Path.ChangeExtension(sourcePath, ".spv");

    CompilationInput input = new()
    {
        language = SourceType.GLSL,
        stage = stage,
        client = ClientType.Vulkan,
        clientVersion = TargetClientVersion.Vulkan_1_3,
        targetLanguage = TargetLanguage.SPV,
        targetLanguageVersion = TargetLanguageVersion.SPV_1_5,
        code = source,
        sourceEntrypoint = "main",
        defaultVersion = 450,
        defaultProfile = ShaderProfile.None,
        forceDefaultVersionAndProfile = true,
        forwardCompatible = false,
        messages = MessageType.SPVRules | MessageType.VulkanRules,
    };

    using Shader shader = new(input);

    if (!shader.Preprocess() || !shader.Parse())
    {
        Console.Error.WriteLine($"Failed to compile {sourcePath}:");
        Console.Error.WriteLine(shader.GetInfoLog());
        Environment.Exit(1);
    }

    using GlslangProgram program = new();
    program.AddShader(shader);

    if (!program.Link(MessageType.SPVRules | MessageType.VulkanRules))
    {
        Console.Error.WriteLine($"Failed to link {sourcePath}:");
        Console.Error.WriteLine(program.GetInfoLog());
        Environment.Exit(1);
    }

    program.GenerateSPIRV(out uint[] words, stage);
    byte[] spirv = new byte[words.Length * sizeof(uint)];
    Buffer.BlockCopy(words, 0, spirv, 0, spirv.Length);
    File.WriteAllBytes(outputPath, spirv);
    Console.WriteLine($"Wrote {outputPath} ({spirv.Length} bytes)");
}
