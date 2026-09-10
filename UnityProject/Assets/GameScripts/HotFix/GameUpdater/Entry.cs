using System;
using Cysharp.Threading.Tasks;
using TEngine;

namespace GameUpdater
{
    /// <summary>
    /// 可热更第二阶段入口。它只依赖 TEngine.Runtime 的稳定合同，不引用业务或主包 Procedure。
    /// </summary>
    public static class Entry
    {
        // A/B release 可以只改变此处状态文案/编排，即可在不替换基础 Player 的情况下观察更新器换版。
        private const string BehaviorMarker = "GameUpdater/v1";

        public static async UniTask<UpdateStageResult> RunAsync(UpdateSessionContext context, IUpdateHost host)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (host == null)
            {
                throw new ArgumentNullException(nameof(host));
            }

            try
            {
                while (host.IsSessionCurrent(context))
                {
                    context.CancellationToken.ThrowIfCancellationRequested();
                    host.ReportStatus(context, $"{BehaviorMarker}: 正在准备第二阶段资源更新...");

                    UpdateDownloadResult download = await host.DownloadRemainingResourcesAsync(context);
                    if (!host.IsSessionCurrent(context))
                    {
                        return UpdateStageResult.Cancelled(context, "Update session is no longer current.");
                    }

                    if (download == null)
                    {
                        return UpdateStageResult.Failed(context, "Host returned a null download result.");
                    }

                    switch (download.Status)
                    {
                        case UpdateDownloadStatus.Succeeded:
                            host.ReportStatus(context, $"{BehaviorMarker}: 第二阶段资源已就绪。");
                            return UpdateStageResult.Ready(context);
                        case UpdateDownloadStatus.Cancelled:
                            return UpdateStageResult.Cancelled(context, download.Error);
                        case UpdateDownloadStatus.Failed:
                            UpdateRetryDecision decision = await host.RequestRetryOrCancelAsync(context, download.Error);
                            if (decision == UpdateRetryDecision.Retry && host.IsSessionCurrent(context))
                            {
                                continue;
                            }

                            return UpdateStageResult.Cancelled(context, download.Error);
                        default:
                            return UpdateStageResult.Failed(context, $"Unknown download result '{download.Status}'.");
                    }
                }

                return UpdateStageResult.Cancelled(context, "Update session ended.");
            }
            catch (OperationCanceledException)
            {
                return UpdateStageResult.Cancelled(context, "Update session was cancelled.");
            }
            catch (Exception exception)
            {
                return UpdateStageResult.Failed(context, exception.ToString());
            }
        }
    }
}
