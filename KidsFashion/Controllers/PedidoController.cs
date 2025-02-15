﻿using AutoMapper;
using KidsFashion.Dominio;
using KidsFashion.Models;
using KidsFashion.Servicos.CadastrosBasicos;
using KidsFashion.Servicos.Relatorios.Clientes;
using KidsFashion.Servicos.Relatorios.Fornecedores;
using KidsFashion.Servicos.Relatorios.Pedidos;
using KidsFashion.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace KidsFashion.Controllers
{
    public class PedidoController : Controller
    {
        private readonly IMapper _mapper;

        public PedidoController(IMapper mapper)
        {
            _mapper = mapper;
        }

        public async Task<IActionResult> IndexAsync()
        {
            var servicoPedido = new ServicoPedido();

            var pedidos = await servicoPedido.ObterTodosCompletoRastreamento();
            
            var retorno = _mapper.Map<List<PedidoViewModel>>(pedidos);

            foreach (var pedido in retorno)
            {
                pedido.ValorTotal = pedido.PedidoProdutos.Sum(c => c.Valor * c.Quantidade);
            }

            return View("Listagem", retorno);
        }

        public async Task<IActionResult> CreateAsync()
        {
            var servicoCliente = new ServicoCliente();
            var servicoProduto = new ServicoProduto();

            var clientes = await servicoCliente.ObterTodos();

            var produtos = await servicoProduto.ObterTodos();

            var vm = new PedidoViewModel()
            {
                ClienteOptions = new SelectList(clientes, "Id", "Nome"),
                ProdutoOptions = new SelectList(produtos, "Id", "Nome")
            };

            return View("Create", vm);
        }

        [HttpPost]
        public async Task<IActionResult> Submit(PedidoViewModel model)
        {
            var servicoProduto = new ServicoProduto();
            var servicoCliente = new ServicoCliente();
            var servicoEstoque = new ServicoEstoque();


            var clientes = await servicoCliente.ObterTodos();
            var produtos = await servicoProduto.ObterTodos();

            model.PedidoProdutos = TempData.ContainsKey("PedidoProdutos")
                ? TempData.Get<List<PedidoProdutoViewModel>>("PedidoProdutos")
                : new List<PedidoProdutoViewModel>();

            var actionType = Request.Form["ActionType"];
            
            if (actionType == "add")
            {
                // Lógica de adicionar produto
                if (model.PedidoProdutos.Any(p => p.Produto_Id == model.Produto_Id))
                {
                    ModelState.AddModelError("", "Este produto já foi adicionado. Remova-o para editar.");
                }
                else if (!ExisteProdutoEmEstoque(model.Produto_Id))
                {
                    ModelState.AddModelError("", "Produto sem estoque disponível.");
                }
                
                else if (model.Produto_Id > 0 && model.Quantidade > 0)
                {
                    var produto = servicoProduto.ObterTodosCompletoRastreamento().Result
                        .FirstOrDefault(c => c.Id == model.Produto_Id);

                    var estoqueProduto = servicoEstoque.ObterTodosFiltro(c => c.Produto_Id == model.Produto_Id).Result.FirstOrDefault();

                    var produtovm = _mapper.Map<ProdutoViewModel>(produto);

                    var pedidoprodutovm = new PedidoProdutoViewModel
                    {
                        Produto_Id = model.Produto_Id,
                        Produto = produtovm,
                        Quantidade = model.Quantidade,
                        Valor = estoqueProduto.PrecoUnitario
                    };

                    model.PedidoProdutos.Add(pedidoprodutovm);
                }
            }
            else if (actionType == "remove")
            {
                // Lógica de remover produto
                var produtoARemover = model.PedidoProdutos.FirstOrDefault(p => p.Produto_Id == model.Produto_Id);
               
                if (produtoARemover != null)
                    model.PedidoProdutos.Remove(produtoARemover);

            }
            else if (actionType == "save")
            {
                if (!model.PedidoProdutos.Any())
                {
                    ModelState.AddModelError("", "Necessário adicionar um item ao menos para salvar o pedido.");
                    model.ClienteOptions = new SelectList(clientes, "Id", "Nome");
                    model.ProdutoOptions = new SelectList(produtos, "Id", "Nome");
                    return View("Create", model);
                }

                var servicoPedido = new ServicoPedido();
                var pedido = new Pedido();
                pedido.Cliente_Id = model.Cliente_Id;
                pedido.DataPedido = model.DataPedido;

                var idPedido = await servicoPedido.AdicionarRetornarID(pedido);

                var servicoPedidoProduto = new ServicoPedidoProduto();

                foreach (var item in model.PedidoProdutos)
                {
                    var pedidoItem = new PedidoProduto();
                    pedidoItem.Pedido_Id = idPedido;
                    pedidoItem.Produto_Id = item.Produto_Id;
                    pedidoItem.Quantidade = item.Quantidade;
                    pedidoItem.Valor = item.Valor;

                    await servicoPedidoProduto.Adicionar(pedidoItem);
                    AtualizarEstoqueAsync(pedidoItem.Produto_Id, pedidoItem.Quantidade);

                }

                // Lógica para salvar ou outra ação
                TempData.Remove("PedidoProdutos");

                return RedirectToAction("Index");
            }

            TempData.Put("PedidoProdutos", model.PedidoProdutos);

            model.ClienteOptions = new SelectList(clientes, "Id", "Nome");
            model.ProdutoOptions = new SelectList(produtos, "Id", "Nome");
            model.ValorTotal = model.PedidoProdutos.Sum(c => c.Valor * c.Quantidade);

            return View("Create", model);
        }

        private async Task AtualizarEstoqueAsync(int ProdutoId, int Quantidade)
        {
            //Atualizar Estoque
            var servicoEstoque = new ServicoEstoque();

            var produtoEmEstoque = servicoEstoque.ObterTodosFiltro(c => c.Produto_Id == ProdutoId).Result.FirstOrDefault();

            produtoEmEstoque.Quantidade -= Quantidade;

            await servicoEstoque.Atualizar(produtoEmEstoque);
        }

        private bool ExisteProdutoEmEstoque(int produto_Id)
        {
            var servicoEstoque = new ServicoEstoque();

            var estoqueProduto = servicoEstoque.ObterTodosFiltro(c => c.Produto_Id == produto_Id).Result.FirstOrDefault();

            if(estoqueProduto != null && estoqueProduto.Quantidade > 0)
                return true;

            return false;
        }

        // Ação para excluir uma categoria
        [HttpPost]
        public async Task<IActionResult> Excluir(long id)
        {
            var servicoPedido = new ServicoPedido();

            await servicoPedido.RemoverPedidoComAtualizacaoEstoqueAsync(id);

            return RedirectToAction("Index");
        }

        // Processa o envio do formulário de edição
        [HttpGet]
        public async Task<IActionResult> Edit(long id)
        {
            var servicoPedido = new ServicoPedido();
            var servicoCliente = new ServicoCliente();
            var servicoProduto = new ServicoProduto();

            var pedido = servicoPedido.ObterTodosCompletoRastreamento().Result.Where(c => c.Id == id).FirstOrDefault();

            var pedidoVm = _mapper.Map<PedidoViewModel>(pedido);

            pedidoVm.ClienteOptions = servicoCliente.ObterTodos().Result.Select(te => new SelectListItem
            {
                Value = te.Id.ToString(),
                Text = te.Nome,
                Selected = te.Id == pedido.Cliente_Id
            }).ToList();

            pedidoVm.ProdutoOptions = servicoProduto.ObterTodos().Result.Select(te => new SelectListItem
            {
                Value = te.Id.ToString(),
                Text = te.Nome
            }).ToList();

            //// Salvar os produtos em TempData
            TempData.Put("PedidoProdutos", pedidoVm.PedidoProdutos);

            pedidoVm.ValorTotal = pedidoVm.PedidoProdutos.Sum(c => c.Valor * c.Quantidade);

            return View("Edit", pedidoVm);
        }

        // Processa o envio do formulário de edição
        [HttpPost]
        public async Task<IActionResult> SubmitEdit(PedidoViewModel model)
        {
            var servicoCliente = new ServicoCliente();
            var servicoProduto = new ServicoProduto();
            var servicoPedido = new ServicoPedido();
            var servicoPedidoProduto = new ServicoPedidoProduto();
            var servicoEstoque = new ServicoEstoque();
            

            var clientes = await servicoCliente.ObterTodos();
            var produtos = await servicoProduto.ObterTodos();

            // Recuperar os produtos do TempData
            model.PedidoProdutos = TempData.ContainsKey("PedidoProdutos")
                ? TempData.Get<List<PedidoProdutoViewModel>>("PedidoProdutos")
                : new List<PedidoProdutoViewModel>();

            var actionType = Request.Form["ActionType"];

            if (actionType == "add")
            {
                // Lógica de adicionar produto
                if (model.PedidoProdutos.Any(p => p.Produto_Id == model.Produto_Id))
                {
                    ModelState.AddModelError("", "Este produto já foi adicionado. Remova-o para editar.");
                }
                else if (!ExisteProdutoEmEstoque(model.Produto_Id))
                {
                    ModelState.AddModelError("", "Produto sem estoque disponível.");
                }
                else if (model.Produto_Id > 0 && model.Quantidade > 0)
                {
                    var produto = servicoProduto.ObterTodosCompletoRastreamento().Result
                        .FirstOrDefault(c => c.Id == model.Produto_Id);

                    var estoqueProduto = servicoEstoque.ObterTodosFiltro(c => c.Produto_Id == model.Produto_Id).Result.FirstOrDefault();

                    var produtovm = _mapper.Map<ProdutoViewModel>(produto);

                    var pedidoprodutovm = new PedidoProdutoViewModel
                    {
                        Produto_Id = model.Produto_Id,
                        Produto = produtovm,
                        Quantidade = model.Quantidade,
                        Valor = estoqueProduto.PrecoUnitario
                    };

                    model.PedidoProdutos.Add(pedidoprodutovm);
                }
            }
            else if (actionType == "remove")
            {
                // Lógica de remover produto
                var produtoARemover = model.PedidoProdutos.FirstOrDefault(p => p.Produto_Id == model.Produto_Id);

                if (produtoARemover != null)
                    model.PedidoProdutos.Remove(produtoARemover);
            }
            else if (actionType == "save")
            {
                if (!model.PedidoProdutos.Any())
                {
                    ModelState.AddModelError("", "Necessário adicionar um item ao menos para salvar o pedido.");
                    model.ClienteOptions = new SelectList(clientes, "Id", "Nome");
                    model.ProdutoOptions = new SelectList(produtos, "Id", "Nome");
                    return View("Edit", model);
                }

                var pedido = servicoPedido.ObterTodosFiltro(c => c.Id == model.Id).Result.FirstOrDefault();
                pedido.DataPedido = model.DataPedido;

                await servicoPedido.Atualizar(pedido);

                await servicoPedido.RemoverItensPedidoComAtualizacaoEstoqueAsync(pedido.Id.Value);

                foreach (var item in model.PedidoProdutos)
                {
                    var pedidoItem = new PedidoProduto();
                    pedidoItem.Pedido_Id = model.Id;
                    pedidoItem.Produto_Id = item.Produto_Id;
                    pedidoItem.Quantidade = item.Quantidade;
                    pedidoItem.Valor = item.Valor;

                    await servicoPedidoProduto.Adicionar(pedidoItem);
                    AtualizarEstoqueAsync(pedidoItem.Produto_Id, pedidoItem.Quantidade);
                }

                // Lógica para salvar ou outra ação
                TempData.Remove("PedidoProdutos");

                return RedirectToAction("Index");
            }

            TempData.Put("PedidoProdutos", model.PedidoProdutos);

            model.ClienteOptions = new SelectList(clientes, "Id", "Nome");
            model.ProdutoOptions = new SelectList(produtos, "Id", "Nome");
            model.ValorTotal = model.PedidoProdutos.Sum(c => c.Valor * c.Quantidade);

            return View("Edit", model);
        }

        public async Task<IActionResult> Imprimir()
        {
            MemoryStream relatorioStream = new MemoryStream();

            var gerador = new RelatorioPedidoGenerator();

            relatorioStream = await gerador.GerarRelatorioPDF();

            Response.Headers.Add("Content-Disposition", "attachment; filename=Pedidos.pdf");
            return File(relatorioStream.ToArray(), "application/pdf");
        }

        public async Task<IActionResult> ImprimirDetalhado(int id)
        {
            MemoryStream relatorioStream = new MemoryStream();

            var gerador = new RelatorioPedidoDetalhadoGenerator(id);

            relatorioStream = await gerador.GerarRelatorioPDF();

            Response.Headers.Add("Content-Disposition", "attachment; filename=PedidoDetalhado.pdf");
            return File(relatorioStream.ToArray(), "application/pdf");
        }

    }
}
