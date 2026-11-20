package com.arcana.firebase;

import android.app.Activity;
import androidx.annotation.NonNull;
import com.android.billingclient.api.*;
import java.util.Collections;
import java.util.List;
import java.util.Set;
import org.godotengine.godot.Godot;
import org.godotengine.godot.plugin.GodotPlugin;
import org.godotengine.godot.plugin.SignalInfo;
import org.godotengine.godot.plugin.UsedByGodot;

/** Compra única no consumible. El servidor valida y confirma el token antes de renovar sin anuncios. */
public final class ArcanaBillingPlugin extends GodotPlugin {
    private Operation active;
    public ArcanaBillingPlugin(Godot godot) { super(godot); }
    @NonNull @Override public String getPluginName() { return "ArcanaBilling"; }
    @NonNull @Override public Set<SignalInfo> getPluginSignals() {
        return Collections.singleton(new SignalInfo("billing_result", String.class, String.class, String.class));
    }
    @UsedByGodot public void beginPurchase(String id, String product, String owner, boolean restore) {
        runOnHostThread(() -> {
            if (active != null || getActivity() == null) { emitSignal("billing_result", id, "unavailable", ""); return; }
            Operation op = new Operation(id, product, owner, restore);
            active = op;
            try { op.connect(getActivity()); } catch (RuntimeException e) { op.finish("unavailable", ""); }
        });
    }
    @UsedByGodot public void cancelPurchase(String id) {
        runOnHostThread(() -> { if (active != null && active.id.equals(id)) active.finish("closed", ""); });
    }
    @Override public void onMainDestroy() {
        if (active != null) active.finish("closed", "");
    }
    private final class Operation implements BillingClientStateListener, PurchasesUpdatedListener {
        final String id, product, owner;
        final boolean restore;
        BillingClient client;
        Operation(String id, String product, String owner, boolean restore) {
            this.id = id; this.product = product; this.owner = owner; this.restore = restore;
        }
        void connect(Activity activity) {
            client = BillingClient.newBuilder(activity).setListener(this).enableAutoServiceReconnection()
                .enablePendingPurchases(PendingPurchasesParams.newBuilder().enableOneTimeProducts().build()).build();
            client.startConnection(this);
        }
        void finish(String status, String token) {
            if (active != this) return;
            active = null;
            if (client != null) client.endConnection();
            emitSignal("billing_result", id, status, token);
        }
        @Override public void onBillingServiceDisconnected() {
            runOnHostThread(() -> finish("unavailable", ""));
        }
        @Override public void onBillingSetupFinished(@NonNull BillingResult result) {
            runOnHostThread(() -> {
                if (active != this) return;
                if (result.getResponseCode() == BillingClient.BillingResponseCode.OK) owned(!restore);
                else finish("unavailable", "");
            });
        }
        boolean select(List<Purchase> purchases) {
            if (purchases == null) return false;
            for (Purchase p : purchases) {
                if (!p.getProducts().contains(product)) continue;
                AccountIdentifiers account = p.getAccountIdentifiers();
                if (account == null || !owner.equals(account.getObfuscatedAccountId())) { finish("other_account", ""); return true; }
                if (p.getPurchaseState() == Purchase.PurchaseState.PURCHASED) finish("purchased", p.getPurchaseToken());
                else finish("pending", "");
                return true;
            }
            return false;
        }
        void owned(boolean launchIfMissing) {
            QueryPurchasesParams query = QueryPurchasesParams.newBuilder().setProductType(BillingClient.ProductType.INAPP).build();
            client.queryPurchasesAsync(query, (result, purchases) -> runOnHostThread(() -> {
                if (active != this) return;
                if (result.getResponseCode() != BillingClient.BillingResponseCode.OK) { finish("unavailable", ""); return; }
                if (select(purchases)) return;
                if (launchIfMissing) checkout(); else finish("none", "");
            }));
        }
        void checkout() {
            QueryProductDetailsParams.Product query = QueryProductDetailsParams.Product.newBuilder()
                .setProductId(product).setProductType(BillingClient.ProductType.INAPP).build();
            client.queryProductDetailsAsync(QueryProductDetailsParams.newBuilder().setProductList(Collections.singletonList(query)).build(),
                (result, details) -> runOnHostThread(() -> {
                    if (active != this) return;
                    if (result.getResponseCode() != BillingClient.BillingResponseCode.OK || getActivity() == null) { finish("unavailable", ""); return; }
                    for (ProductDetails item : details.getProductDetailsList()) {
                        if (!product.equals(item.getProductId())) continue;
                        List<ProductDetails.OneTimePurchaseOfferDetails> offers = item.getOneTimePurchaseOfferDetailsList();
                        if (offers == null) continue;
                        for (ProductDetails.OneTimePurchaseOfferDetails offer : offers) {
                            if (offer.getRentalDetails() != null || offer.getPreorderDetails() != null) continue;
                            BillingFlowParams.ProductDetailsParams entry = BillingFlowParams.ProductDetailsParams.newBuilder()
                                .setProductDetails(item).setOfferToken(offer.getOfferToken()).build();
                            BillingResult started = client.launchBillingFlow(getActivity(), BillingFlowParams.newBuilder()
                                .setObfuscatedAccountId(owner).setProductDetailsParamsList(Collections.singletonList(entry)).build());
                            if (started.getResponseCode() == BillingClient.BillingResponseCode.ITEM_ALREADY_OWNED) owned(false);
                            else if (started.getResponseCode() != BillingClient.BillingResponseCode.OK) finish("unavailable", "");
                            return;
                        }
                    }
                    finish("unavailable", "");
                }));
        }
        @Override public void onPurchasesUpdated(@NonNull BillingResult result, List<Purchase> purchases) {
            runOnHostThread(() -> {
                if (active != this) return;
                switch (result.getResponseCode()) {
                    case BillingClient.BillingResponseCode.OK: if (!select(purchases)) finish("none", ""); break;
                    case BillingClient.BillingResponseCode.ITEM_ALREADY_OWNED: owned(false); break;
                    case BillingClient.BillingResponseCode.USER_CANCELED: finish("closed", ""); break;
                    default: finish("unavailable", "");
                }
            });
        }
    }
}
