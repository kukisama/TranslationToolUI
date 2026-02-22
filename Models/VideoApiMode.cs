namespace TrueFluentPro.Models
{
    /// <summary>
    /// 视频生成 API 模式。
    /// 
    /// 说明：Azure OpenAI 目前在不同模型/版本上存在两类路径：
    /// - Sora Jobs: /openai/v1/video/generations/jobs (异步轮询 + generations[].id 下载)
    /// - Videos: /openai/v1/videos (更接近 OpenAI 兼容的 videos 接口)
    /// </summary>
    public enum VideoApiMode
    {
        /// <summary>
        /// Sora 异步 Jobs 模式：/openai/v1/video/generations/jobs
        /// </summary>
        SoraJobs = 0,

        /// <summary>
        /// Videos 接口：/openai/v1/videos
        /// </summary>
        Videos = 1
    }
}
