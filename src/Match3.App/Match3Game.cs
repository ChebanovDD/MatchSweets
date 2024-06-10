using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using Match3.App.Interfaces;
using Match3.App.Internal;
using Match3.Core.Interfaces;
using Match3.Core.Structs;

namespace Match3.App
{
    public abstract class Match3Game<TGridSlot> : BaseGame<TGridSlot> where TGridSlot : IGridSlot
    {
        private readonly JobsExecutor _jobsExecutor;
        private readonly IItemSwapper<TGridSlot> _itemSwapper;

        private AsyncLazy _swapItemsTask;
        private IBoardFillStrategy<TGridSlot> _fillStrategy;

        protected Match3Game(GameConfig<TGridSlot> config) : base(config)
        {
            _itemSwapper = config.ItemSwapper;
            _jobsExecutor = new JobsExecutor();
        }

        protected bool IsSwapItemsCompleted
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _swapItemsTask == null || _swapItemsTask.Task.Status.IsCompleted();
        }

        public async UniTask StartAsync(CancellationToken cancellationToken = default)
        {
            if (_fillStrategy == null)
            {
                throw new NullReferenceException(nameof(_fillStrategy));
            }

            await FillAsync(_fillStrategy, cancellationToken);

            StartGame();
        }

        public async UniTask StopAsync()
        {
            if (IsSwapItemsCompleted == false)
            {
                await _swapItemsTask;
            }

            StopGame();
        }

        public void SetGameBoardFillStrategy(IBoardFillStrategy<TGridSlot> fillStrategy)
        {
            _fillStrategy = fillStrategy;
        }

        protected override void OnAllGoalsAchieved()
        {
            RaiseGameFinishedAsync().Forget();
        }

        protected UniTask SwapItemsAsync(GridPosition position1, GridPosition position2,
            CancellationToken cancellationToken = default)
        {
            if (_swapItemsTask?.Task.Status.IsCompleted() ?? true)
            {
                _swapItemsTask = SwapItemsAsync(_fillStrategy, position1, position2, cancellationToken).ToAsyncLazy();
            }

            return _swapItemsTask.Task;
        }

        private async UniTask FillAsync(IBoardFillStrategy<TGridSlot> fillStrategy,
            CancellationToken cancellationToken = default)
        {
            await ExecuteJobsAsync(fillStrategy.GetFillJobs(GameBoard), cancellationToken);
        }

        protected virtual async UniTask SwapItemsAsync(IBoardFillStrategy<TGridSlot> fillStrategy, GridPosition position1,
            GridPosition position2, CancellationToken cancellationToken = default)
        {
            await SwapGameBoardItemsAsync(position1, position2, cancellationToken);

            if (IsSolved(position1, position2, out var solvedData))
            {
                NotifySequencesSolved(solvedData);
                await ExecuteJobsAsync(fillStrategy.GetSolveJobs(GameBoard, solvedData), cancellationToken);

                while (IsSolveExistOnBoard(out solvedData))
                {
                    await ExecuteJobsAsync(fillStrategy.GetSolveJobs(GameBoard, solvedData), cancellationToken);
                }
            }
            else
            {
                await SwapGameBoardItemsAsync(position1, position2, cancellationToken);
            }
        }

        protected async UniTask SwapGameBoardItemsAsync(GridPosition position1, GridPosition position2,
            CancellationToken cancellationToken = default)
        {
            var gridSlot1 = GameBoard[position1.RowIndex, position1.ColumnIndex];
            var gridSlot2 = GameBoard[position2.RowIndex, position2.ColumnIndex];

            await _itemSwapper.SwapItemsAsync(gridSlot1, gridSlot2, cancellationToken);
        }

        protected UniTask ExecuteJobsAsync(IEnumerable<IJob> jobs, CancellationToken cancellationToken = default)
        {
            return _jobsExecutor.ExecuteJobsAsync(jobs, cancellationToken);
        }

        private async UniTask RaiseGameFinishedAsync()
        {
            if (IsSwapItemsCompleted == false)
            {
                await _swapItemsTask;
            }

            base.OnAllGoalsAchieved();
        }
    }
}
